using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Runtime;

namespace RhinoGhMcp.Rhino
{
    /// <summary>
    /// TCP-JSON listener that exposes Rhino document operations to the
    /// rhino-gh-mcp Python server. Singleton — only one listener can bind
    /// the port at a time.
    ///
    /// Wire protocol (matches the bridge in /server/src/rhino_gh_mcp/bridges/rhino.py):
    ///
    ///   Client → server:  {"type": "<command>", "params": {...}}
    ///                     then closes the write half of the connection.
    ///   Server → client:  {"status": "success"|"error", "result": <any>, "message": "..."}
    ///
    /// One request per connection. Read until EOF, then write reply, then close.
    /// Document mutations are marshalled onto the Rhino UI thread.
    /// </summary>
    public class McpService
    {
        public const int Port = 9876;
        public const string Host = "127.0.0.1";

        private static readonly Lazy<McpService> _instance = new Lazy<McpService>(() => new McpService());
        public static McpService Instance => _instance.Value;

        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private volatile bool _running;

        public bool IsRunning => _running;

        private McpService() { }

        public void Start()
        {
            if (_running)
            {
                RhinoApp.WriteLine("rhino-gh-mcp: already running.");
                return;
            }
            try
            {
                _cts = new CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Parse(Host), Port);
                _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listener.Start();
                _running = true;
                _ = Task.Run(() => AcceptLoop(_cts.Token));
            }
            catch (Exception ex)
            {
                _running = false;
                RhinoApp.WriteLine($"rhino-gh-mcp: failed to start: {ex.Message}");
            }
        }

        public void Stop()
        {
            if (!_running) return;
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            _running = false;
        }

        private async Task AcceptLoop(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    TcpClient client;
                    try
                    {
                        client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException) { break; }
                    catch (InvalidOperationException) { break; }
                    _ = Task.Run(() => HandleClient(client, token));
                }
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"rhino-gh-mcp: accept loop error: {ex.Message}");
            }
        }

        private async Task HandleClient(TcpClient client, CancellationToken token)
        {
            using (client)
            {
                try
                {
                    client.NoDelay = true;
                    using var stream = client.GetStream();
                    // Read until EOF — the Python bridge calls shutdown(SHUT_WR) after sending,
                    // so we can drain into a buffer reliably.
                    using var ms = new MemoryStream();
                    var buf = new byte[64 * 1024];
                    int read;
                    while ((read = await stream.ReadAsync(buf, 0, buf.Length, token).ConfigureAwait(false)) > 0)
                        ms.Write(buf, 0, read);

                    string requestJson = Encoding.UTF8.GetString(ms.ToArray());
                    string responseJson = HandleCommand(requestJson);

                    var respBytes = Encoding.UTF8.GetBytes(responseJson);
                    await stream.WriteAsync(respBytes, 0, respBytes.Length, token).ConfigureAwait(false);
                    await stream.FlushAsync(token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    RhinoApp.WriteLine($"rhino-gh-mcp: client handler error: {ex.Message}");
                }
            }
        }

        // ---------- Dispatch ------------------------------------------------

        private string HandleCommand(string requestJson)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(requestJson))
                    return Err("Empty request body");

                JObject cmd;
                try { cmd = JObject.Parse(requestJson); }
                catch (JsonException ex) { return Err($"Invalid JSON: {ex.Message}"); }

                string type = (string)cmd["type"] ?? "";
                JObject p = (cmd["params"] as JObject) ?? new JObject();

                switch (type)
                {
                    case "is_server_available": return IsServerAvailable();
                    case "get_scene_info":      return GetSceneInfo();
                    case "get_layers":          return GetLayers();
                    case "get_objects_with_metadata": return GetObjectsWithMetadata(p);
                    case "capture_viewport":    return CaptureViewport(p);
                    case "execute_code":        return ExecuteCode(p);
                    case "run_named_command":   return RunNamedCommand(p);
                    default: return Err($"Unknown command type: {type}");
                }
            }
            catch (Exception ex)
            {
                return Err($"Dispatch error: {ex.Message}");
            }
        }

        // ---------- Handlers -----------------------------------------------

        private string IsServerAvailable()
        {
            string asmVersion = "?";
            string asmLocation = "?";
            try
            {
                var asm = typeof(McpService).Assembly;
                asmVersion = asm.GetName().Version?.ToString() ?? "?";
                asmLocation = asm.Location ?? "?";
            }
            catch { }

            return Ok(new JObject
            {
                ["available"] = true,
                ["host"] = Host,
                ["port"] = Port,
                ["plugin_name"] = "rhino-gh-mcp-rhino",
                ["plugin_version"] = asmVersion,
                ["assembly_location"] = asmLocation,
                ["rhino_version"] = RhinoApp.Version?.ToString() ?? "?",
            });
        }

        private string GetSceneInfo()
        {
            return OnUiThread(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return Err("No active document.");

                var layers = new JArray();
                foreach (var layer in doc.Layers)
                {
                    var objs = doc.Objects.FindByLayer(layer.Name) ?? Array.Empty<RhinoObject>();
                    var samples = new JArray();
                    foreach (var obj in objs.Take(5))
                    {
                        samples.Add(SerializeObject(obj, doc, includeMetadata: false));
                    }
                    layers.Add(new JObject
                    {
                        ["full_path"] = layer.FullPath,
                        ["object_count"] = objs.Length,
                        ["is_visible"] = layer.IsVisible,
                        ["is_locked"] = layer.IsLocked,
                        ["sample_objects"] = samples
                    });
                }
                return Ok(new JObject
                {
                    ["doc_name"] = doc.Name ?? "",
                    ["doc_path"] = doc.Path ?? "",
                    ["layers"] = layers
                });
            });
        }

        private string GetLayers()
        {
            return OnUiThread(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return Err("No active document.");

                var layers = new JArray();
                foreach (var layer in doc.Layers)
                {
                    layers.Add(new JObject
                    {
                        ["index"] = layer.Index,
                        ["name"] = layer.Name,
                        ["full_path"] = layer.FullPath,
                        ["is_visible"] = layer.IsVisible,
                        ["is_locked"] = layer.IsLocked,
                        ["color"] = ColorToHex(layer.Color)
                    });
                }
                return Ok(new JObject { ["layers"] = layers });
            });
        }

        private string GetObjectsWithMetadata(JObject p)
        {
            return OnUiThread(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return Err("No active document.");

                var filters = p["filters"] as JObject ?? new JObject();
                string layerFilter = (string)filters["layer"];
                string nameFilter = (string)filters["name"];
                string idFilter = (string)filters["short_id"];

                List<string> metadataFields = null;
                if (p["metadata_fields"] is JArray arr)
                    metadataFields = arr.Select(t => t.ToString()).ToList();

                var matched = new JArray();
                foreach (var obj in doc.Objects)
                {
                    if (!MatchesFilter(obj, doc, layerFilter, nameFilter, idFilter)) continue;
                    matched.Add(SerializeObject(obj, doc, includeMetadata: true, projection: metadataFields));
                }
                return Ok(new JObject
                {
                    ["count"] = matched.Count,
                    ["objects"] = matched
                });
            });
        }

        private string CaptureViewport(JObject p)
        {
            return OnUiThread(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null) return Err("No active document.");
                var view = doc.Views.ActiveView;
                if (view == null) return Err("No active viewport.");

                int maxSize = p["max_size"]?.Value<int>() ?? 800;

                using var bmp = view.CaptureToBitmap();
                if (bmp == null) return Err("CaptureToBitmap returned null.");

                int w = bmp.Width, h = bmp.Height;
                int newW = w, newH = h;
                if (Math.Max(w, h) > maxSize)
                {
                    double scale = (double)maxSize / Math.Max(w, h);
                    newW = Math.Max(1, (int)(w * scale));
                    newH = Math.Max(1, (int)(h * scale));
                }

                using var resized = new System.Drawing.Bitmap(bmp, new System.Drawing.Size(newW, newH));
                using var msImg = new MemoryStream();
                resized.Save(msImg, System.Drawing.Imaging.ImageFormat.Png);
                string b64 = Convert.ToBase64String(msImg.ToArray());

                return Ok(new JObject
                {
                    ["data"] = b64,
                    ["format"] = "png",
                    ["original_width"] = w,
                    ["original_height"] = h,
                    ["width"] = newW,
                    ["height"] = newH
                });
            });
        }

        private string ExecuteCode(JObject p)
        {
            string code = (string)p["code"];
            if (string.IsNullOrEmpty(code))
                return Err("execute_code requires a 'code' parameter.");

            return OnUiThread(() =>
            {
                try
                {
                    var script = PythonScript.Create();
                    if (script == null)
                        return Err("PythonScript.Create() returned null (Rhino's Python runtime unavailable).");

                    // Capture stdout via Rhino's CommandWindow surrogate where possible.
                    // PythonScript executes inline. Errors propagate as exceptions.
                    object result = null;
                    string capturedOutput = null;
                    try
                    {
                        script.Output = (s) =>
                        {
                            capturedOutput = (capturedOutput ?? string.Empty) + s;
                        };
                    }
                    catch { /* Output hook differs across Rhino versions — best effort */ }

                    bool ok = script.ExecuteScript(code);
                    try { result = script.GetVariable("result"); } catch { }

                    return Ok(new JObject
                    {
                        ["executed"] = ok,
                        ["result"] = result != null ? JToken.FromObject(result.ToString()) : null,
                        ["stdout"] = capturedOutput
                    });
                }
                catch (Exception ex)
                {
                    return Err($"execute_code failed: {ex.Message}");
                }
            });
        }

        private string RunNamedCommand(JObject p)
        {
            string command = (string)p["command"];
            bool echo = p["echo"]?.Value<bool>() ?? false;
            if (string.IsNullOrEmpty(command))
                return Err("run_named_command requires a 'command' parameter (e.g. '_SelAll').");

            return OnUiThread(() =>
            {
                try
                {
                    bool ok = RhinoApp.RunScript(command, echo);
                    return Ok(new JObject { ["command"] = command, ["success"] = ok });
                }
                catch (Exception ex) { return Err($"RunScript failed: {ex.Message}"); }
            });
        }

        // ---------- Helpers -----------------------------------------------

        private static JObject SerializeObject(
            RhinoObject obj, RhinoDoc doc, bool includeMetadata, List<string> projection = null)
        {
            var json = new JObject
            {
                ["id"] = obj.Id.ToString(),
                ["name"] = obj.Name ?? "",
                ["type"] = obj.Geometry?.GetType().Name ?? "Unknown",
                ["layer"] = doc.Layers[obj.Attributes.LayerIndex]?.FullPath ?? ""
            };
            if (includeMetadata)
            {
                var meta = new JObject();
                foreach (var key in obj.Attributes.GetUserStrings().AllKeys ?? Array.Empty<string>())
                {
                    if (projection != null && !projection.Contains(key)) continue;
                    meta[key] = obj.Attributes.GetUserString(key);
                }
                if (meta.Count > 0) json["metadata"] = meta;
            }
            return json;
        }

        private static bool MatchesFilter(RhinoObject obj, RhinoDoc doc,
            string layerFilter, string nameFilter, string idFilter)
        {
            if (!string.IsNullOrEmpty(layerFilter))
            {
                string layerName = doc.Layers[obj.Attributes.LayerIndex]?.FullPath ?? "";
                if (!WildcardMatch(layerName, layerFilter)) return false;
            }
            if (!string.IsNullOrEmpty(nameFilter))
            {
                if (!WildcardMatch(obj.Name ?? "", nameFilter)) return false;
            }
            if (!string.IsNullOrEmpty(idFilter))
            {
                string s = obj.Attributes.GetUserString("short_id") ?? "";
                if (s != idFilter) return false;
            }
            return true;
        }

        private static bool WildcardMatch(string value, string pattern)
        {
            if (!pattern.Contains('*') && !pattern.Contains('?'))
                return value.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
            var rx = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(
                value, rx, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        private static string ColorToHex(System.Drawing.Color c) =>
            $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        // Marshal a function to Rhino's UI thread and wait for the JSON result.
        private static string OnUiThread(Func<string> fn)
        {
            string result = null;
            Exception caught = null;
            var done = new ManualResetEventSlim(false);
            try
            {
                RhinoApp.InvokeOnUiThread(new Action(() =>
                {
                    try { result = fn(); }
                    catch (Exception ex) { caught = ex; }
                    finally { done.Set(); }
                }));
            }
            catch (Exception ex)
            {
                // Fall back to running on the current thread if InvokeOnUiThread fails
                try { return fn(); }
                catch (Exception inner) { return Err($"UI thread invoke failed: {ex.Message}; fallback: {inner.Message}"); }
            }
            done.Wait(15000);
            if (caught != null) return Err($"Handler exception: {caught.Message}");
            return result ?? Err("Handler timed out.");
        }

        private static string Ok(JToken result) =>
            new JObject { ["status"] = "success", ["result"] = result }.ToString(Formatting.None);

        private static string Err(string message) =>
            new JObject { ["status"] = "error", ["message"] = message }.ToString(Formatting.None);
    }
}
