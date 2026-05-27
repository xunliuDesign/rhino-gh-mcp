using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;
using System.Linq;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Rhino;
using Rhino.Geometry;
using Grasshopper.Kernel;
using System.IO;
using System.Collections.Concurrent;
using Grasshopper.Kernel.Types;

namespace RhinoGhMcp
{
    public class McpServerComponent : GH_Component
    {
        /// <summary>
        /// MCP Server component. Drop one on the Grasshopper canvas, toggle Run = True,
        /// and the Python MCP server in /server/ talks to it over loopback HTTP on
        /// the port configured below (default 9999).
        /// </summary>
        public McpServerComponent()
          : base("rhino-gh-mcp Server (v1)", "MCPv1",
            "Hosts the rhino-gh-mcp v1 command bridge for the Python MCP server. " +
            "Replaces the v0 'rhino_gh_mcp MCP Server' component — uninstall the " +
            "v0 .gha or you'll get duplicates in the ribbon.",
            "MCP", "Server")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            // v0.1.5: inputs reorganised around two orthogonal axes:
            //   - CAPABILITIES (what the LLM is allowed to do): AllowParameters / AllowComponents / AllowScripting
            //   - SCOPE (which components are placeable, when AllowComponents=True): ComponentScope + CategoryFilter
            // The legacy SetParameterMode input was dropped (panel mode is the only
            // path used in practice).
            pManager.AddBooleanParameter("RunServer", "Run", "Start/stop the MCP server", GH_ParamAccess.item, false);

            pManager.AddBooleanParameter("AllowParameters", "AllowParams",
                "Allow the LLM to adjust sliders, toggles, value-lists, and panels.",
                GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("AllowComponents", "AllowComp",
                "Allow the LLM to place / wire / remove components (subject to ComponentScope).",
                GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("AllowScripting", "AllowScript",
                "Allow the LLM to write code into Script components and execute code in the bridge. " +
                "Powerful and risky - off by default.",
                GH_ParamAccess.item, false);

            pManager.AddIntegerParameter("ComponentScope", "Scope",
                "When AllowComponents=True, which components are placeable: " +
                "0 = curated (only categories listed in CategoryFilter); " +
                "1 = gh defaults (stock Grasshopper components only); " +
                "2 = all (everything, including third-party plug-ins).",
                GH_ParamAccess.item, 1);
            pManager.AddTextParameter("CategoryFilter", "Filter",
                "Comma-separated category names allowed when ComponentScope=0 (curated). " +
                "Example: 'MCP, Curve, Surface'. Ignored at higher scopes.",
                GH_ParamAccess.item, "MCP");

            pManager.AddBooleanParameter("AutoRecompute", "AutoRecomp",
                "Automatically recompute all after each command execution.",
                GH_ParamAccess.item, false);
            pManager.AddIntegerParameter("Port", "Port",
                "TCP port the MCP HTTP listener binds to. Must match the Python server's --gh-port.",
                GH_ParamAccess.item, 9999);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            // Use the pManager object to register your output parameters.
            // Output parameters do not have default values, but they too must have the correct access type.
            pManager.AddTextParameter("Status", "Status", "Server status", GH_ParamAccess.item);
            pManager.AddTextParameter("DebugOutput", "Debug", "Debug/sticky info", GH_ParamAccess.item);
            // v1 NEW: explicit version output so you can verify which plugin is loaded
            pManager.AddTextParameter("Version", "Version", "Plugin version + commit identifier", GH_ParamAccess.item);

            // Sometimes you want to hide a specific parameter from the Rhino preview.
            // You can use the HideParameter() method as a quick way:
            //pManager.HideParameter(0);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Inputs (v0.1.5 ordering): Run, AllowParameters, AllowComponents,
            // AllowScripting, ComponentScope, CategoryFilter, AutoRecompute, Port.
            bool run = false;
            bool allowParams = true;
            bool allowComponents = true;
            bool allowScripting = false;
            int componentScope = 0;
            string filter = "MCP";
            bool autoRecompute = false;
            int portInput = 9999;
            if (!DA.GetData(0, ref run)) return;
            DA.GetData(1, ref allowParams);
            DA.GetData(2, ref allowComponents);
            DA.GetData(3, ref allowScripting);
            DA.GetData(4, ref componentScope);
            DA.GetData(5, ref filter);
            DA.GetData(6, ref autoRecompute);
            DA.GetData(7, ref portInput);

            currentAllowParameters = allowParams;
            currentAllowComponents = allowComponents;
            currentAllowScripting = allowScripting;
            currentComponentScope = Math.Max(0, Math.Min(2, componentScope));
            currentCategoryFilter = filter;
            currentAutoRecompute = autoRecompute;

            // Apply port from input. Only takes effect when the server isn't already running;
            // changing the port while running won't rebind — toggle Run off and on.
            if (serverThread == null || !serverThread.IsAlive)
                port = portInput;

            // Start/stop server logic
            if (run && (serverThread == null || !serverThread.IsAlive))
            {
                runServer = true;
                serverThread = new Thread(ServerThreadLoop);
                serverThread.IsBackground = true;
                serverThread.Start();
                serverStatus = "Server starting...";
            }
            else if (!run && serverThread != null && serverThread.IsAlive)
            {
                runServer = false;
                serverStatus = "Server stopping...";
                // --- Forcibly stop the listener and thread ---
                lock (serverLock)
                {
                    if (listener != null)
                    {
                        try { listener.Stop(); } catch { }
                        listener = null;
                    }
                }
                // Give thread more time to exit gracefully - never use Thread.Abort()!
                if (!serverThread.Join(3000))
                {
                    // Log but don't abort - Thread.Abort() causes ExecutionEngineException
                    LogError("Server thread did not stop gracefully within timeout");
                }
                serverThread = null;
            }
            DA.SetData(0, serverStatus);
            DA.SetData(1, debugOutput);
            DA.SetData(2, PluginVersionString);
        }

        // v1 NEW: build a version string from the assembly so users can sanity-check
        // which plugin Grasshopper actually loaded.
        private static readonly string PluginVersionString = BuildVersionString();
        private static string BuildVersionString()
        {
            try
            {
                var asm = typeof(McpServerComponent).Assembly;
                var name = asm.GetName();
                return string.Format("rhino-gh-mcp v{0} ({1})",
                    name.Version != null ? name.Version.ToString() : "0.0.0",
                    "https://github.com/xunliuDesign/rhino-gh-mcp");
            }
            catch
            {
                return "rhino-gh-mcp v? (https://github.com/xunliuDesign/rhino-gh-mcp)";
            }
        }
        /// <summary>
        /// The Exposure property controls where in the panel a component icon 
        /// will appear. There are seven possible locations (primary to septenary), 
        /// each of which can be combined with the GH_Exposure.obscure flag, which 
        /// ensures the component will only be visible on panel dropdowns.
        /// </summary>
        public override GH_Exposure Exposure => GH_Exposure.primary;

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// You can add image files to your project resources and access them like this:
        /// return Resources.IconForThisComponent;
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream("RhinoGhMcp.Resources.Icon.png"))
                {
                    if (stream != null)
                        return new System.Drawing.Bitmap(stream);
                    return null;
                }
            }
        }

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        // Fresh v1 GUID — intentionally different from the v0 component
        // (f1795527-33da-4992-b55a-220f2b17f1dc) so the two plugins coexist as
        // distinct components in the ribbon and you can tell which is loaded.
        public override Guid ComponentGuid => new Guid("005a98bf-a9f8-4e11-a96a-ea4eafb59c4c");

        // --- Server State ---
        private Thread serverThread = null;
        private volatile bool runServer = false;
        private string serverStatus = "Server Off";
        private string debugOutput = "";
        private int port = 9999;
        private string host = "127.0.0.1";
        private string lastError = null;
        private TcpListener listener = null;
        private object serverLock = new object();
        private string currentCategoryFilter = "MCP";

        // v0.1.5: capability + scope state (settable from canvas inputs).
        private bool currentAllowParameters = true;
        private bool currentAllowComponents = true;
        private bool currentAllowScripting = false;
        // 0 = curated (CategoryFilter), 1 = gh defaults, 2 = all
        private int currentComponentScope = 1;

        private bool currentAutoRecompute = false;

        // === Persistent Debug Log and Error System ===
        private static ConcurrentQueue<string> debugLog = new ConcurrentQueue<string>();
        private void LogDebug(string msg) { debugLog.Enqueue($"[{DateTime.Now:HH:mm:ss}] {msg}"); if (debugLog.Count > 1000) debugLog.TryDequeue(out _); }
        private void LogError(string msg) { lastError = $"[{DateTime.Now:HH:mm:ss}] {msg}"; LogDebug("ERROR: " + msg); }
        private void LogError(string context, string msg) { lastError = $"[{DateTime.Now:HH:mm:ss}] [{context}] {msg}"; LogDebug($"ERROR [{context}]: {msg}"); }
        private JObject GetDebugLog(JObject cmd)
        {
            var arr = new JArray(debugLog.ToArray());
            return SuccessResponse(new JObject { ["log"] = arr, ["lastError"] = lastError });
        }

        private JObject IsServerAvailable(JObject cmd)
        {
            // Simple check - if we're here processing the command, the server is available.
            // v0.1.2: include the live assembly version + location so callers can verify
            // exactly which .gha Grasshopper actually loaded (useful when ribbon caches
            // mislead and you suspect a stale build is hanging around).
            string asmLocation = "?";
            string asmVersion = "?";
            try
            {
                var asm = typeof(McpServerComponent).Assembly;
                asmVersion = asm.GetName().Version != null ? asm.GetName().Version.ToString() : "?";
                asmLocation = asm.Location ?? "?";
            }
            catch { /* fall back to "?" */ }
            return SuccessResponse(new JObject
            {
                ["available"] = true,
                ["status"] = serverStatus,
                ["host"] = host,
                ["port"] = port,
                ["plugin_name"] = "rhino-gh-mcp",
                ["plugin_version"] = asmVersion,
                ["assembly_location"] = asmLocation,
                ["component_guid"] = "005a98bf-a9f8-4e11-a96a-ea4eafb59c4c"
            });
        }

        // --- Server Thread Loop (scaffold) ---
        private void ServerThreadLoop()
        {
            try
            {
                serverStatus = $"Listening on {host}:{port}";
                ExpireComponentSolution();
                lock (serverLock)
                {
                    listener = new TcpListener(IPAddress.Parse(host), port);
                    listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    listener.Start();
                }
                while (runServer)
                {
                    try
                    {
                        // Use async accept pattern for better cancellation
                        var tcpClientTask = listener.AcceptTcpClientAsync();

                        // Poll for completion or cancellation
                        while (!tcpClientTask.IsCompleted)
                        {
                            if (!runServer || listener == null)
                            {
                                // Cancel the accept operation
                                try { listener.Stop(); } catch { }
                                return;
                            }
                            Thread.Sleep(50);
                        }

                        if (!runServer) break;

                        TcpClient client = tcpClientTask.Result;
                        ThreadPool.QueueUserWorkItem(HandleClient, client);
                    }
                    catch (ObjectDisposedException)
                    {
                        // Expected when listener is stopped
                        break;
                    }
                    catch (InvalidOperationException)
                    {
                        // Listener was stopped
                        break;
                    }
                    catch (Exception ex)
                    {
                        LogError("Accept", ex.Message);
                        if (!runServer) break;
                        Thread.Sleep(100); // Brief pause before retry
                    }
                }
                lock (serverLock)
                {
                    if (listener != null)
                    {
                        listener.Stop();
                        listener = null;
                    }
                }
                serverStatus = "Server stopped.";
                ExpireComponentSolution();
            }
            catch (Exception ex)
            {
                lastError = ex.ToString();
                serverStatus = "Server error: " + ex.Message;
                ExpireComponentSolution();
                lock (serverLock)
                {
                    if (listener != null)
                    {
                        try { listener.Stop(); } catch { }
                        listener = null;
                    }
                }
            }
        }

        // --- Handle Client (full implementation) ---
        private void HandleClient(object obj)
        {
            TcpClient client = obj as TcpClient;
            if (client == null) return;
            try
            {
                using (NetworkStream stream = client.GetStream())
                {
                    // --- Read HTTP-like request ---
                    var reader = new StreamReader(stream, Encoding.UTF8);
                    var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                    string requestLine = reader.ReadLine();
                    if (string.IsNullOrEmpty(requestLine)) { client.Close(); return; }
                    // Read headers
                    string line;
                    int contentLength = 0;
                    while (!string.IsNullOrEmpty(line = reader.ReadLine()))
                    {
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        {
                            int.TryParse(line.Substring(15).Trim(), out contentLength);
                        }
                    }
                    // Read body
                    string body = "";
                    if (contentLength > 0)
                    {
                        char[] buffer = new char[contentLength];
                        int read = 0;
                        while (read < contentLength)
                        {
                            int n = reader.Read(buffer, read, contentLength - read);
                            if (n <= 0) break;
                            read += n;
                        }
                        body = new string(buffer, 0, read);
                    }
                    // --- Parse and dispatch command ---
                    JObject response = new JObject();
                    try
                    {
                        JObject cmd = null;
                        if (!string.IsNullOrWhiteSpace(body))
                            cmd = JObject.Parse(body);
                        else
                            cmd = new JObject();
                        string type = (string)cmd["type"] ?? "unknown";
                        response = DispatchCommand(type, cmd);
                    }
                    catch (Exception ex)
                    {
                        response["status"] = "error";
                        response["result"] = "Command parse/dispatch error: " + ex.Message;
                    }
                    // --- Write HTTP-like response ---
                    string respBody = response.ToString(Formatting.None);
                    string httpResp =
                      "HTTP/1.1 200 OK\r\n" +
                      "Content-Type: application/json; charset=utf-8\r\n" +
                      $"Content-Length: {Encoding.UTF8.GetByteCount(respBody)}\r\n" +
                      "Access-Control-Allow-Origin: *\r\n" +
                      "Connection: close\r\n\r\n" +
                      respBody;
                    byte[] respBytes = Encoding.UTF8.GetBytes(httpResp);
                    stream.Write(respBytes, 0, respBytes.Length);
                }
            }
            catch (Exception ex)
            {
                lastError = ex.ToString();
            }
            finally
            {
                try { client.Close(); } catch { }
            }
        }

        // --- Command Dispatch ---
        private JObject DispatchCommand(string type, JObject cmd)
        {
            try
            {
                // v0.1.5: capability gate. Commands that mutate the canvas are
                // grouped by which canvas-input flag they require. If the flag
                // is off, refuse with a clean message naming the knob to flip.
                if (_parameterWriteCommands.Contains(type) && !currentAllowParameters)
                    return CapabilityDenied("AllowParameters");
                if (_componentWriteCommands.Contains(type) && !currentAllowComponents)
                    return CapabilityDenied("AllowComponents");
                if (_scriptingCommands.Contains(type) && !currentAllowScripting)
                    return CapabilityDenied("AllowScripting");

                JObject result = null;
                switch (type)
                {
                    case "add_component_to_canvas":
                        result = AddComponentToCanvas(cmd);
                        break;
                    case "add_slider_to_canvas":
                        result = AddSliderToCanvas(cmd);
                        break;
                    case "get_context":
                        result = GetContext(cmd);
                        break;
                    case "expire_component":
                        result = ExpireComponent(cmd);
                        break;
                    case "get_object":
                    case "get_objects":
                        result = GetObjects(cmd);
                        break;
                    case "get_selected":
                        result = GetSelected(cmd);
                        break;
                    case "update_script":
                        result = UpdateScript(cmd);
                        break;
                    case "update_script_with_code_reference":
                        result = UpdateScriptWithCodeReference(cmd);
                        break;
                    case "connect_components":
                        result = ConnectComponents(cmd);
                        break;
                    case "remove_node":
                        result = RemoveNode(cmd);
                        break;
                    case "recompute_all":
                        result = RecomputeAll(cmd);
                        break;
                    case "get_all_component_proxies":
                        result = GetAllComponentProxies(cmd);
                        break;
                    case "get_all_component_library":
                        result = GetAllComponentLibrary(cmd);
                        break;
                    case "set_component_parameter":
                        result = SetComponentParameter(cmd);
                        break;
                    case "execute_code":
                        result = ExecuteCode(cmd);
                        break;
                    case "get_debug_log":
                        result = GetDebugLog(cmd);
                        break;
                    case "is_server_available":
                        result = IsServerAvailable(cmd);
                        break;
                    case "get_panel_content":
                        result = GetPanelContent(cmd);
                        break;
                    // v0.1.1 additions ----------------------------------------
                    case "set_slider_range":
                        result = SetSliderRange(cmd);
                        break;
                    case "get_runtime_messages":
                        result = GetRuntimeMessages(cmd);
                        break;
                    case "capture_canvas":
                        result = CaptureCanvas(cmd);
                        break;
                    // v0.1.4 additions ----------------------------------------
                    case "set_toggle_value":
                        result = SetToggleValue(cmd);
                        break;
                    case "set_value_list_selection":
                        result = SetValueListSelection(cmd);
                        break;
                    // v0.1.5: expose current capability state to the Python server.
                    case "get_capabilities":
                        result = GetCapabilities(cmd);
                        break;
                    default:
                        return ErrorResponse($"Unknown command type: {type}");
                }

                // Auto-recompute if enabled and command was successful
                if (currentAutoRecompute && result != null && result["status"]?.ToString() == "success")
                {
                    // Don't auto-recompute for certain commands that don't modify the document
                    var readOnlyCommands = new[] { "get_context", "get_objects", "get_selected", "get_all_component_proxies",
                                                    "get_all_component_library", "get_debug_log", "is_server_available", "get_panel_content",
                                                    "get_capabilities", "get_runtime_messages" };
                    if (!readOnlyCommands.Contains(type))
                    {
                        var recomputeResult = RecomputeAll(new JObject());
                        LogDebug($"Auto-recomputed after command: {type}");
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                return ErrorResponse($"Dispatch error: {ex.Message}");
            }
        }

        // --- Command Handlers (stubs, to be filled in) ---
        private JObject AddComponentToCanvas(JObject cmd)
        {
            string name = (string)cmd["component_name"] ?? (string)cmd["name"];
            int x = (int?)(cmd["position_x"] ?? 100) ?? 100;
            int y = (int?)(cmd["position_y"] ?? 100) ?? 100;
            // v0.1.1: L3 escape hatch — when true, ignore CategoryFilter and place any component.
            bool bypassFilter = (bool?)(cmd["bypass_filter"] ?? false) ?? false;
            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);

            RunOnUiThread(() =>
            {
                try
                {
                    var doc = GetGHDocument();
                    var server = Grasshopper.Instances.ComponentServer;

                    // v0.1.5: ComponentScope decides which proxies are even
                    // candidates. bypassFilter (from gh_add_any_component)
                    // forces ALL scope for the single call.
                    //   0 = curated:  intersect with CategoryFilter
                    //   1 = defaults: only proxies whose assembly is part of Grasshopper itself
                    //   2 = all:      no filter
                    int effectiveScope = bypassFilter ? 2 : currentComponentScope;
                    List<string> categories = null;
                    if (effectiveScope == 0 && !string.IsNullOrWhiteSpace(currentCategoryFilter))
                    {
                        categories = currentCategoryFilter.Split(',')
                            .Select(c => c.Trim())
                            .Where(c => !string.IsNullOrEmpty(c))
                            .ToList();
                    }
                    Func<IGH_ObjectProxy, bool> scopePredicate;
                    if (effectiveScope == 0)
                    {
                        scopePredicate = (p) => categories == null || categories.Count == 0
                            || categories.Any(cat => (p.Desc.Category ?? "").Equals(cat, StringComparison.OrdinalIgnoreCase));
                    }
                    else if (effectiveScope == 1)
                    {
                        // Stock Grasshopper components live in assemblies whose
                        // simple name starts with "Grasshopper" (Grasshopper.dll,
                        // Grasshopper.GUI, etc). Third-party plug-ins do not.
                        scopePredicate = (p) =>
                        {
                            try
                            {
                                string asmName = p.GetType().Assembly.GetName().Name ?? "";
                                return asmName.StartsWith("Grasshopper", StringComparison.OrdinalIgnoreCase);
                            }
                            catch { return false; }
                        };
                    }
                    else
                    {
                        scopePredicate = (p) => true;
                    }

                    var proxy = server.ObjectProxies.FirstOrDefault(p =>
                        p?.Desc != null &&
                        scopePredicate(p) &&
                        (p.Desc.Name == name || p.Desc.NickName == name)
                    );
                    if (proxy == null)
                    {
                        string scopeDesc = effectiveScope == 2 ? "ANY scope"
                                         : effectiveScope == 1 ? "Grasshopper-default scope"
                                         : $"curated scope (categories: {currentCategoryFilter})";
                        result = new JObject { ["status"] = "error", ["result"] = $"Component '{name}' not found in {scopeDesc}." };
                    }
                    else
                    {
                        var comp = proxy.CreateInstance();
                        comp.CreateAttributes();
                        comp.Attributes.Pivot = new System.Drawing.PointF(x, y);
                        doc.AddObject(comp, false);
                        result = new JObject { ["status"] = "success", ["result"] = $"Component '{proxy.Desc.Name}' added at ({x},{y}) in category '{proxy.Desc.Category}'" };
                    }
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    done.Set();
                }
            });

            done.Wait(5000); // Wait up to 5 seconds
            if (error != null) return ErrorResponse($"Error adding component: {error.Message}");
            return result ?? ErrorResponse("Operation timed out");
        }
        private JObject AddSliderToCanvas(JObject cmd)
        {
            string name = (string)cmd["name"] ?? "Slider";
            double min = (double?)(cmd["min_value"] ?? 0.0) ?? 0.0;
            double max = (double?)(cmd["max_value"] ?? 10.0) ?? 10.0;
            double val = (double?)(cmd["value"] ?? 1.0) ?? 1.0;
            int x = (int?)(cmd["position_x"] ?? 100) ?? 100;
            int y = (int?)(cmd["position_y"] ?? 100) ?? 100;
            bool integer = (bool?)(cmd["integer"] ?? false) ?? false;
            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);

            RunOnUiThread(() =>
            {
                try
                {
                    var doc = GetGHDocument();
                    var slider = new GH_NumberSlider();
                    slider.CreateAttributes();
                    slider.NickName = name;
                    slider.Slider.Minimum = (decimal)min;
                    slider.Slider.Maximum = (decimal)max;
                    slider.Slider.Value = (decimal)val;
                    slider.Slider.DecimalPlaces = integer ? 0 : 2;
                    slider.Attributes.Pivot = new System.Drawing.PointF(x, y);
                    doc.AddObject(slider, false);
                    result = new JObject { ["status"] = "success", ["result"] = $"Slider '{name}' added at ({x},{y})" };
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    done.Set();
                }
            });

            done.Wait(5000);
            if (error != null) return ErrorResponse($"Error adding slider: {error.Message}");
            return result ?? ErrorResponse("Operation timed out");
        }
        private JObject GetContext(JObject cmd)
        {
            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);

            RunOnUiThread(() =>
            {
                try
                {
                    var doc = GetGHDocument();
                    var all = new JObject();
                    foreach (var obj in doc.Objects)
                    {
                        if (obj is IGH_Component comp)
                            all[comp.InstanceGuid.ToString()] = GetComponentInfo(comp);
                        else if (obj is IGH_Param param && param.Attributes?.Parent == null)
                            all[param.InstanceGuid.ToString()] = GetParamInfo(param, false, null, false);
                    }
                    result = new JObject { ["status"] = "success", ["result"] = all };
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    done.Set();
                }
            });

            done.Wait(5000);
            if (error != null) return ErrorResponse($"Error getting context: {error.Message}");
            return result ?? ErrorResponse("Operation timed out");
        }
        private JObject ExpireComponent(JObject cmd)
        {
            string guid = (string)cmd["instance_guid"];
            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);

            RunOnUiThread(() =>
            {
                try
                {
                    var doc = GetGHDocument();
                    var obj = doc.FindObject(new Guid(guid), true);
                    if (obj == null)
                    {
                        result = new JObject { ["status"] = "error", ["result"] = "Object not found." };
                        return;
                    }
                    obj.ExpireSolution(true);
                    if (obj is IGH_Component comp)
                    {
                        result = new JObject { ["status"] = "success", ["result"] = GetComponentInfo(comp) };
                    }
                    else if (obj is IGH_Param param)
                    {
                        result = new JObject { ["status"] = "success", ["result"] = GetParamInfo(param, false, null, false) };
                    }
                    else
                    {
                        result = new JObject { ["status"] = "success", ["result"] = "Component expired." };
                    }
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    done.Set();
                }
            });

            done.Wait(5000);
            if (error != null) return ErrorResponse($"Error expiring component: {error.Message}");
            return result ?? ErrorResponse("Operation timed out");
        }
        private JObject GetObjects(JObject cmd)
        {
            JArray guids = (JArray)(cmd["instance_guids"] ?? new JArray());
            if (cmd["instance_guid"] != null) guids.Add(cmd["instance_guid"]);
            int depth = (int?)(cmd["context_depth"] ?? 0) ?? 0;
            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);

            RunOnUiThread(() =>
            {
                try
                {
                    var doc = GetGHDocument();
                    var all = new JObject();
                    var set = new HashSet<string>(guids.Select(g => g.ToString()));
                    foreach (var obj in doc.Objects)
                    {
                        if (obj is IGH_Component comp && set.Contains(comp.InstanceGuid.ToString()))
                            all[comp.InstanceGuid.ToString()] = GetComponentInfo(comp);
                        else if (obj is IGH_Param param && param.Attributes?.Parent == null && set.Contains(param.InstanceGuid.ToString()))
                            all[param.InstanceGuid.ToString()] = GetParamInfo(param, false, null, false);
                    }
                    result = new JObject { ["status"] = "success", ["result"] = all };
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    done.Set();
                }
            });

            done.Wait(5000);
            if (error != null) return ErrorResponse($"Error getting objects: {error.Message}");
            return result ?? ErrorResponse("Operation timed out");
        }
        private JObject GetSelected(JObject cmd)
        {
            int depth = (int?)(cmd["context_depth"] ?? 0) ?? 0;
            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);

            RunOnUiThread(() =>
            {
                try
                {
                    var doc = GetGHDocument();
                    var all = new JObject();
                    foreach (var obj in doc.Objects)
                    {
                        bool sel = obj.Attributes?.Selected ?? false;
                        if (sel)
                        {
                            if (obj is IGH_Component comp)
                                all[comp.InstanceGuid.ToString()] = GetComponentInfo(comp);
                            else if (obj is IGH_Param param && param.Attributes?.Parent == null)
                                all[param.InstanceGuid.ToString()] = GetParamInfo(param, false, null, true);
                        }
                    }
                    result = new JObject { ["status"] = "success", ["result"] = all };
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    done.Set();
                }
            });

            done.Wait(5000);
            if (error != null) return ErrorResponse($"Error getting selected: {error.Message}");
            return result ?? ErrorResponse("Operation timed out");
        }
        private JObject UpdateScript(JObject cmd)
        {
            string guid = (string)cmd["instance_guid"];
            string code = (string)cmd["code"];
            string desc = (string)cmd["description"];
            string msg = (string)cmd["message_to_user"];
            JArray paramDefs = (JArray)cmd["param_definitions"];
            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);

            RunOnUiThread(() =>
            {
                try
                {
                    var doc = GetGHDocument();
                    var obj = doc.FindObject(new Guid(guid), false) as IGH_Component;
                    if (obj == null || !obj.GetType().GetProperty("Code")?.CanWrite == true)
                    {
                        result = new JObject { ["status"] = "error", ["result"] = "Not a script component." };
                        return;
                    }
                    if (paramDefs != null) { /* param update logic omitted for brevity */ }
                    if (code != null) obj.GetType().GetProperty("Code").SetValue(obj, code, null);
                    if (desc != null) obj.Description = desc;
                    result = new JObject { ["status"] = "success", ["result"] = "Script updated." };
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    done.Set();
                }
            });

            done.Wait(5000);
            if (error != null) return ErrorResponse($"Error updating script: {error.Message}");
            return result ?? ErrorResponse("Operation timed out");
        }
        private JObject UpdateScriptWithCodeReference(JObject cmd)
        {
            string guid = (string)cmd["instance_guid"];
            string file = (string)cmd["file_path"];
            JArray paramDefs = (JArray)cmd["param_definitions"];
            string desc = (string)cmd["description"];
            string name = (string)cmd["name"];
            bool force = (bool?)(cmd["force_code_reference"] ?? false) ?? false;
            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);

            RunOnUiThread(() =>
            {
                try
                {
                    var doc = GetGHDocument();
                    var obj = doc.FindObject(new Guid(guid), false) as IGH_Component;
                    if (obj == null || !obj.GetType().GetProperty("InputIsPath")?.CanWrite == true)
                    {
                        result = new JObject { ["status"] = "error", ["result"] = "Not a script component." };
                        return;
                    }
                    if (force) obj.GetType().GetProperty("InputIsPath").SetValue(obj, true, null);
                    if (desc != null) obj.Description = desc;
                    if (name != null) obj.NickName = name;
                    result = new JObject { ["status"] = "success", ["result"] = "Script code reference updated." };
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    done.Set();
                }
            });

            done.Wait(5000);
            if (error != null) return ErrorResponse($"Error updating script reference: {error.Message}");
            return result ?? ErrorResponse("Operation timed out");
        }
        private JObject ConnectComponents(JObject cmd)
        {
            string srcGuid = (string)cmd["source_guid"];
            string srcOut = (string)cmd["source_output"];
            string tgtGuid = (string)cmd["target_guid"];
            string tgtIn = (string)cmd["target_input"];
            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);

            RunOnUiThread(() =>
            {
                try
                {
                    var doc = GetGHDocument();
                    var src = doc.FindObject(new Guid(srcGuid), true);
                    var tgt = doc.FindObject(new Guid(tgtGuid), true) as IGH_Component;
                    if (src == null || tgt == null)
                    {
                        result = new JObject { ["status"] = "error", ["result"] = "Source or target not found." };
                        return;
                    }
                    IGH_Param srcParam = null;
                    if (src is GH_NumberSlider) srcParam = src as IGH_Param;
                    else if (src is IGH_Component sc) srcParam = sc.Params.Output.FirstOrDefault(p => p.NickName == srcOut || p.Name == srcOut);
                    var tgtParam = tgt.Params.Input.FirstOrDefault(p => p.NickName == tgtIn || p.Name == tgtIn);
                    if (srcParam == null || tgtParam == null)
                    {
                        result = new JObject { ["status"] = "error", ["result"] = "Params not found." };
                        return;
                    }
                    while (tgtParam.Sources.Count > 0) tgtParam.RemoveSource(tgtParam.Sources[0]);
                    tgtParam.AddSource(srcParam);
                    result = new JObject { ["status"] = "success", ["result"] = "Connected." };
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    done.Set();
                }
            });

            done.Wait(5000);
            if (error != null) return ErrorResponse($"Error connecting components: {error.Message}");
            return result ?? ErrorResponse("Operation timed out");
        }
        private JObject RemoveNode(JObject cmd)
        {
            string guid = (string)cmd["instance_guid"];
            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);

            RunOnUiThread(() =>
            {
                try
                {
                    var doc = GetGHDocument();
                    var obj = doc.FindObject(new Guid(guid), true);
                    if (obj == null)
                    {
                        result = new JObject { ["status"] = "error", ["result"] = "Object not found." };
                        return;
                    }
                    doc.RemoveObject(obj, true);
                    result = new JObject { ["status"] = "success", ["result"] = "Removed." };
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    done.Set();
                }
            });

            done.Wait(5000);
            if (error != null) return ErrorResponse($"Error removing node: {error.Message}");
            return result ?? ErrorResponse("Operation timed out");
        }
        private JObject RecomputeAll(JObject cmd)
        {
            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);

            RunOnUiThread(() =>
            {
                try
                {
                    GetGHDocument().NewSolution(true);
                    result = new JObject { ["status"] = "success", ["result"] = "Recomputed." };
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    done.Set();
                }
            });

            done.Wait(5000);
            if (error != null) return ErrorResponse($"Error recomputing: {error.Message}");
            return result ?? ErrorResponse("Operation timed out");
        }
        private JObject GetAllComponentProxies(JObject cmd)
        {
            int limit = (int?)(cmd["limit"] ?? 1000) ?? 1000;
            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);

            try
            {
                RunOnUiThread(() =>
                {
                    try
                    {
                        var server = Grasshopper.Instances.ComponentServer;
                        if (server?.ObjectProxies == null)
                        {
                            result = new JObject { ["status"] = "error", ["error"] = "Component server not initialized" };
                            return;
                        }

                        // Support multiple categories separated by commas
                        List<string> categories = null;
                        if (!string.IsNullOrWhiteSpace(currentCategoryFilter))
                        {
                            categories = currentCategoryFilter.Split(',')
                                .Select(c => c.Trim())
                                .Where(c => !string.IsNullOrEmpty(c))
                                .ToList();
                        }
                        int retries = 0;
                        int maxRetries = 3;
                        int proxyCount = 0;
                        List<IGH_ObjectProxy> proxies = null;
                        while (retries < maxRetries)
                        {
                            try
                            {
                                proxies = server.ObjectProxies
                                    .Where(p => p?.Desc != null &&
                                        (
                                            categories == null || categories.Count == 0
                                            ? true // No filter, fetch all
                                            : categories.Any(cat => (p.Desc.Category ?? "").Equals(cat, StringComparison.OrdinalIgnoreCase))
                                        )
                                    )
                                    .Take(limit)
                                    .ToList();
                                proxyCount = proxies.Count;
                                LogDebug($"[GetAllComponentProxies] Attempt {retries + 1}: Found {proxyCount} proxies.");
                                if (proxyCount > 0) break;
                            }
                            catch (Exception ex)
                            {
                                LogError("GetProxies", $"Attempt {retries + 1} failed: {ex.Message}");
                            }
                            System.Threading.Thread.Sleep(200);
                            retries++;
                        }
                        if (proxyCount == 0) LogError("[GetAllComponentProxies] No proxies found after retries.");

                        // Group by Category and SubCategory
                        var grouped = new JObject();
                        if (proxies != null)
                        {
                            foreach (var proxy in proxies)
                            {
                                var desc = proxy.Desc;
                                string category = desc.Category ?? "Uncategorized";
                                string subcategory = desc.SubCategory ?? "Unspecified";

                                // Build proxy info with extra fields
                                var proxyObj = new JObject
                                {
                                    ["Name"] = desc.Name,
                                    ["NickName"] = desc.NickName,
                                    ["Guid"] = proxy.Guid.ToString(),
                                    ["Description"] = desc.Description,
                                    ["Category"] = desc.Category,
                                    ["SubCategory"] = desc.SubCategory,
                                    ["HasCategory"] = desc.HasCategory,
                                    ["HasSubCategory"] = desc.HasSubCategory,
                                    ["InstanceDescription"] = desc.InstanceDescription,
                                    ["InstanceGuid"] = desc.InstanceGuid.ToString(),
                                    ["Keywords"] = desc.Keywords != null ? JArray.FromObject(desc.Keywords) : null,
                                    ["Kind"] = proxy.Kind != null ? proxy.Kind.ToString() : null,
                                    ["Location"] = proxy.Location != null ? proxy.Location.ToString() : null,
                                    ["SDKCompliant"] = proxy.SDKCompliant,
                                    ["Type"] = proxy.Type != null ? proxy.Type.ToString() : null
                                };
                                // Try to get library info if possible
                                try
                                {
                                    var lib = Grasshopper.Instances.ComponentServer.FindAssemblyByObject(proxy.Guid);
                                    proxyObj["Library"] = lib != null ? lib.Name : null;
                                    proxyObj["LibraryId"] = lib != null ? lib.Id.ToString() : null;
                                    proxyObj["LibraryVersion"] = lib != null ? lib.Version.ToString() : null;
                                }
                                catch { }

                                if (!(grouped[category] is JObject catObj))
                                {
                                    catObj = new JObject();
                                    grouped[category] = catObj;
                                }
                                if (!catObj.ContainsKey(subcategory))
                                {
                                    catObj[subcategory] = new JArray();
                                }
                              ((JArray)catObj[subcategory]).Add(proxyObj);
                            }
                        }

                        result = new JObject { ["status"] = "success", ["result"] = grouped };
                        if (grouped.Count == 0)
                        {
                            LogError("[GetAllComponentProxies] Grouped result is empty ({}). Possible initialization issue.");
                            result = new JObject { ["status"] = "error", ["error"] = "No component proxies found. Grasshopper may not be fully initialized or there is a plugin loading issue." };
                        }
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }
                    finally
                    {
                        done.Set();
                    }
                });

                done.Wait(10000); // Wait up to 10 seconds for UI thread

                if (error != null)
                {
                    LogError("GetAllComponentProxies", $"Exception: {error.Message}");
                    return ErrorResponse($"Exception in GetAllComponentProxies: {error.Message}");
                }

                return result ?? ErrorResponse("Operation timed out");

            }
            catch (Exception ex)
            {
                LogError("GetAllComponentProxies", $"Outer exception: {ex.Message}");
                return ErrorResponse($"Exception in GetAllComponentProxies: {ex.Message}");
            }
        }
        private JObject GetAllComponentLibrary(JObject cmd)
        {
            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);

            RunOnUiThread(() =>
            {
                try
                {
                    var ribbon = Grasshopper.Instances.ComponentServer.CompleteRibbonLayout;
                    var comps = new JArray();
                    foreach (var tab in ribbon.Tabs)
                        foreach (var panel in tab.Panels)
                            foreach (var obj in Grasshopper.Instances.ComponentServer.ObjectProxies)
                                if ((obj.Desc.Category == tab.Name) && (obj.Desc.SubCategory == panel.Name))
                                    comps.Add(new JObject
                                    {
                                        ["Category"] = tab.Name,
                                        ["SubCategory"] = panel.Name,
                                        ["Name"] = obj.Desc.Name,
                                        ["NickName"] = obj.Desc.NickName,
                                        ["Description"] = obj.Desc.Description,
                                        ["Guid"] = obj.Desc.InstanceGuid.ToString()
                                    });
                    result = new JObject { ["status"] = "success", ["result"] = comps };
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    done.Set();
                }
            });

            done.Wait(5000);
            if (error != null) return ErrorResponse($"Error getting component library: {error.Message}");
            return result ?? ErrorResponse("Operation timed out");
        }
        private JObject GetComponentInfo(IGH_Component comp)
        {
            var o = new JObject();
            o["instanceGuid"] = comp.InstanceGuid.ToString();
            o["name"] = comp.Name;
            o["nickName"] = comp.NickName;
            o["description"] = comp.Description;
            o["category"] = comp.Category;
            o["subCategory"] = comp.SubCategory;
            o["kind"] = comp.GetType().Name;
            o["isSelected"] = comp.Attributes?.Selected ?? false;
            o["Inputs"] = new JArray(comp.Params.Input.Select(p => GetParamInfo(p, true, comp.InstanceGuid, false)));
            o["Outputs"] = new JArray(comp.Params.Output.Select(p => GetParamInfo(p, false, comp.InstanceGuid, false)));
            return o;
        }
        private JObject GetParamInfo(IGH_Param param, bool isInput, Guid? parentGuid, bool isSelected)
        {
            var o = new JObject();
            o["instanceGuid"] = param.InstanceGuid.ToString();
            o["parentInstanceGuid"] = parentGuid?.ToString();
            o["name"] = param.Name;
            o["nickName"] = param.NickName;
            o["description"] = param.Description;
            o["kind"] = param.GetType().Name;
            o["isInput"] = isInput;
            o["isSelected"] = isSelected;
            o["access"] = param.Access.ToString();
            o["optional"] = param.Optional;
            o["sources"] = new JArray(param.Sources.Select(s => s.InstanceGuid.ToString()));
            o["targets"] = new JArray(param.Recipients.Select(r => r.InstanceGuid.ToString()));
            AppendWidgetValue(o, param);
            return o;
        }

        // v0.1.3: surface the current value/state of canvas widgets so the LLM
        // can see them via get_context without a separate per-component call.
        // Older builds emitted only static metadata; gh_list_sliders etc.
        // expect these fields.
        private void AppendWidgetValue(JObject o, IGH_Param param)
        {
            try
            {
                if (param is GH_NumberSlider slider)
                {
                    o["value"] = (double)slider.Slider.Value;
                    o["min"] = (double)slider.Slider.Minimum;
                    o["max"] = (double)slider.Slider.Maximum;
                    o["decimalPlaces"] = slider.Slider.DecimalPlaces;
                    o["sliderType"] = slider.Slider.Type.ToString();
                }
                else if (param is GH_BooleanToggle toggle)
                {
                    o["value"] = toggle.Value;
                }
                else if (param is GH_ValueList vl)
                {
                    var items = new JArray();
                    foreach (var item in vl.ListItems)
                    {
                        items.Add(new JObject
                        {
                            ["name"] = item.Name,
                            ["expression"] = item.Expression,
                        });
                    }
                    o["items"] = items;
                    var selected = new JArray();
                    foreach (var item in vl.ListItems)
                    {
                        if (item.Selected) selected.Add(item.Name);
                    }
                    o["selectedItems"] = selected;
                    o["listMode"] = vl.ListMode.ToString();
                }
                else if (param is GH_Panel panel)
                {
                    o["userText"] = panel.UserText;
                }
            }
            catch
            {
                // Never let value extraction break the wider get_context call.
            }
        }

        // --- Utility: Success/Error Response ---
        private JObject SuccessResponse(object result)
        {
            return new JObject { ["status"] = "success", ["result"] = JToken.FromObject(result) };
        }
        private JObject ErrorResponse(string message)
        {
            return new JObject { ["status"] = "error", ["result"] = message };
        }

        // --- Utility: UI Thread Marshal ---
        private void RunOnUiThread(Action action)
        {
            RhinoApp.InvokeOnUiThread(action);
        }

        // --- Utility: Get Grasshopper Document ---
        private GH_Document GetGHDocument()
        {
            return OnPingDocument();
        }

        // --- Utility: Expire this component's solution from any thread (SAFELY)
        private void ExpireComponentSolution()
        {
            try
            {
                // Check if document is still valid before expiring
                RhinoApp.InvokeOnUiThread(new Action(() =>
                {
                    try
                    {
                        if (this.OnPingDocument() != null)
                            this.ExpireSolution(false); // Use false to avoid recursive expiration
                    }
                    catch { }
                }));
            }
            catch { }
        }

        // --- Utility: Get Component/Param Info (stub) ---
        // TODO: Implement info extraction helpers mirroring Python

        // === MCP Server: Command Handlers and Utilities Refactor ===
        // (1) Add set_component_parameter command
        // (2) Refactor for clarity
        // (3) Upgrade update_script and update_script_with_code_reference
        // (4) Add execute_code (optional)
        // (5) Enhance info extraction
        // (6) Add comments
        private JObject SetComponentParameter(JObject cmd)
        {
            string guid = (string)cmd["instance_guid"];
            string paramName = (string)cmd["param_name"];
            string value = (string)cmd["value"];
            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);

            RunOnUiThread(() =>
            {
                try
                {
                    var doc = GetGHDocument();
                    var obj = doc.FindObject(new Guid(guid), true);
                    if (obj == null)
                    {
                        result = new JObject { ["status"] = "error", ["result"] = "Object not found." };
                        return;
                    }
                    IGH_Param param = null;
                    if (obj is IGH_Component comp)
                    {
                        param = comp.Params.Input.FirstOrDefault(p => p.Name == paramName || p.NickName == paramName)
                             ?? comp.Params.Output.FirstOrDefault(p => p.Name == paramName || p.NickName == paramName);
                    }
                    else if (obj is IGH_Param p)
                    {
                        param = p;
                    }
                    if (param == null)
                    {
                        result = new JObject { ["status"] = "error", ["result"] = $"Parameter '{paramName}' not found." };
                        return;
                    }
                    // v0.1.5+: panel mode is the only supported behavior.
                    // Earlier versions also had a "VolatileData" mode and an
                    // "interactive UI" auto-create-widget mode behind a
                    // SetParameterMode input; both were niche v0 cruft and
                    // were removed when the input was dropped. If you need
                    // them back, restore from git history at commit 7e6fe90.
                    IGH_Param panelParam = null;
                    if (param.Sources.Count == 1 && param.Sources[0] is GH_Panel existingPanel)
                    {
                        existingPanel.UserText = value;
                        // Set background to white and minimize size
                        existingPanel.Properties.Colour = System.Drawing.Color.White;
                        existingPanel.Attributes?.ExpireLayout();
                        panelParam = existingPanel;
                    }
                    else
                    {
                        var panel = new GH_Panel();
                        panel.CreateAttributes();
                        panel.UserText = value;
                        // Set background to white and minimize size
                        panel.Properties.Colour = System.Drawing.Color.White;
                        panel.Attributes?.ExpireLayout();
                        panel.Attributes.Pivot = new System.Drawing.PointF(param.Attributes.Pivot.X - 120, param.Attributes.Pivot.Y);
                        doc.AddObject(panel, false);
                        param.RemoveAllSources();
                        param.AddSource(panel);
                        panelParam = panel;
                    }
                    result = new JObject { ["status"] = "success", ["result"] = $"Panel value set to '{value}' and connected to parameter '{paramName}' (Panel mode)" };
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    done.Set();
                }
            });

            done.Wait(5000);
            if (error != null) return ErrorResponse($"Error setting parameter: {error.Message}");
            return result ?? ErrorResponse("Operation timed out");
        }

        private JObject ExecuteCode(JObject cmd)
        {
            // Not supported in C# context
            return ErrorResponse("execute_code is not supported in C# MCP server.");
        }

        // v0.1.5: command-name -> capability-bucket mappings, used by
        // DispatchCommand to short-circuit out-of-capability calls.
        private static readonly HashSet<string> _parameterWriteCommands = new HashSet<string>
        {
            "set_component_parameter",
            "set_slider_range",
            "set_toggle_value",
            "set_value_list_selection",
        };
        private static readonly HashSet<string> _componentWriteCommands = new HashSet<string>
        {
            "add_component_to_canvas",
            "add_slider_to_canvas",
            "connect_components",
            "remove_node",
        };
        private static readonly HashSet<string> _scriptingCommands = new HashSet<string>
        {
            "update_script",
            "update_script_with_code_reference",
            "execute_code",
        };

        // Shared denial response - mirrors the wording from the Python
        // capabilities.denial_message helper so the LLM gets consistent
        // guidance regardless of which side rejected the call.
        private JObject CapabilityDenied(string knob)
        {
            return new JObject
            {
                ["status"] = "error",
                ["result"] = $"Capability denied by canvas: set the `{knob}` input " +
                             "on the rhino-gh-mcp Server component to True.",
                ["denied_by"] = "canvas",
                ["required_capability"] = knob,
            };
        }

        // v0.1.5: report the live canvas-level capability state. The Python
        // CapabilitiesProvider polls this and uses it to drive its runtime gate.
        private JObject GetCapabilities(JObject cmd)
        {
            string scope = currentComponentScope == 2 ? "all"
                         : currentComponentScope == 1 ? "defaults"
                         : "curated";
            return new JObject
            {
                ["status"] = "success",
                ["result"] = new JObject
                {
                    ["allow_parameters"] = currentAllowParameters,
                    ["allow_components"] = currentAllowComponents,
                    ["allow_scripting"] = currentAllowScripting,
                    ["component_scope"] = scope,
                    ["category_filter"] = currentCategoryFilter,
                    ["plugin_version"] = PluginVersionString,
                },
            };
        }

        // v0.1.4: direct write to a top-level Boolean Toggle widget. The
        // generic set_component_parameter handler can't flip a toggle that
        // lives on the canvas as its own object (not as an input source) -
        // it only knows how to attach sources to component inputs.
        private JObject SetToggleValue(JObject cmd)
        {
            string guid = (string)cmd["instance_guid"];
            bool value = (bool?)cmd["value"] ?? false;
            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);

            RunOnUiThread(() =>
            {
                try
                {
                    var doc = GetGHDocument();
                    var obj = doc.FindObject(new Guid(guid), true);
                    if (obj is GH_BooleanToggle toggle)
                    {
                        toggle.Value = value;
                        toggle.ExpireSolution(true);
                        result = new JObject
                        {
                            ["status"] = "success",
                            ["result"] = new JObject
                            {
                                ["instance_guid"] = guid,
                                ["value"] = value,
                            },
                        };
                    }
                    else
                    {
                        result = new JObject
                        {
                            ["status"] = "error",
                            ["result"] = obj == null
                                ? "Object not found."
                                : $"Object is not a GH_BooleanToggle (kind={obj.GetType().Name}).",
                        };
                    }
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    done.Set();
                }
            });

            done.Wait(5000);
            if (error != null) return ErrorResponse($"Error setting toggle: {error.Message}");
            return result ?? ErrorResponse("Operation timed out");
        }

        // v0.1.4: select an item in a top-level Value List by name OR by
        // integer index. Single-select wins for ListMode.DropDown; for the
        // multi-select modes (CheckList) we still set just one and clear
        // any other selections, which is the common LLM-driven workflow.
        private JObject SetValueListSelection(JObject cmd)
        {
            string guid = (string)cmd["instance_guid"];
            // `item` can be a string (match by Name) or an int (match by index)
            JToken itemToken = cmd["item"];
            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);

            RunOnUiThread(() =>
            {
                try
                {
                    var doc = GetGHDocument();
                    var obj = doc.FindObject(new Guid(guid), true);
                    if (!(obj is GH_ValueList vl))
                    {
                        result = new JObject
                        {
                            ["status"] = "error",
                            ["result"] = obj == null
                                ? "Object not found."
                                : $"Object is not a GH_ValueList (kind={obj.GetType().Name}).",
                        };
                        return;
                    }

                    int? targetIndex = null;
                    string targetName = null;
                    if (itemToken == null)
                    {
                        result = new JObject { ["status"] = "error", ["result"] = "Missing `item`." };
                        return;
                    }
                    if (itemToken.Type == JTokenType.Integer)
                    {
                        targetIndex = (int)itemToken;
                    }
                    else
                    {
                        targetName = (string)itemToken;
                        if (int.TryParse(targetName, out int parsed)) targetIndex = parsed;
                    }

                    int matchIndex = -1;
                    if (targetIndex.HasValue && targetIndex.Value >= 0 && targetIndex.Value < vl.ListItems.Count)
                    {
                        matchIndex = targetIndex.Value;
                    }
                    else if (targetName != null)
                    {
                        for (int i = 0; i < vl.ListItems.Count; i++)
                        {
                            if (string.Equals(vl.ListItems[i].Name, targetName, StringComparison.Ordinal))
                            {
                                matchIndex = i;
                                break;
                            }
                        }
                    }

                    if (matchIndex < 0)
                    {
                        var available = string.Join(", ", vl.ListItems.Select(li => li.Name));
                        result = new JObject
                        {
                            ["status"] = "error",
                            ["result"] = $"Item '{itemToken}' not found. Available: [{available}]",
                        };
                        return;
                    }

                    for (int i = 0; i < vl.ListItems.Count; i++)
                    {
                        vl.ListItems[i].Selected = (i == matchIndex);
                    }
                    vl.ExpireSolution(true);

                    result = new JObject
                    {
                        ["status"] = "success",
                        ["result"] = new JObject
                        {
                            ["instance_guid"] = guid,
                            ["selected_index"] = matchIndex,
                            ["selected_name"] = vl.ListItems[matchIndex].Name,
                        },
                    };
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    done.Set();
                }
            });

            done.Wait(5000);
            if (error != null) return ErrorResponse($"Error setting value-list selection: {error.Message}");
            return result ?? ErrorResponse("Operation timed out");
        }

        private JObject GetPanelContent(JObject cmd)
        {
            string guid = (string)cmd["instance_guid"];
            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);

            RunOnUiThread(() =>
            {
                try
                {
                    var doc = GetGHDocument();
                    var obj = doc.FindObject(new Guid(guid), true);
                    
                    if (obj == null)
                    {
                        result = new JObject { ["status"] = "error", ["result"] = "Object not found." };
                        return;
                    }

                    // Check if it's a Panel
                    if (obj is GH_Panel panel)
                    {
                        string userText = panel.UserText;
                        var runtimeContent = new JArray();
                        
                        // Get runtime content from volatile data
                        if (panel.VolatileData != null && panel.VolatileData.DataCount > 0)
                        {
                            try
                            {
                                // Iterate through all paths in the volatile data
                                foreach (var path in panel.VolatileData.Paths)
                                {
                                    var branch = panel.VolatileData.get_Branch(path);
                                    if (branch != null)
                                    {
                                        for (int i = 0; i < branch.Count; i++)
                                        {
                                            var item = branch[i];
                                            if (item != null)
                                            {
                                                runtimeContent.Add(new JObject
                                                {
                                                    ["path"] = path.ToString(),
                                                    ["index"] = i,
                                                    ["value"] = item.ToString(),
                                                    ["type"] = item.GetType().Name
                                                });
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                LogError("GetPanelContent", $"Error reading volatile data: {ex.Message}");
                            }
                        }
                        
                        // Get the formatted text that would be displayed in the panel
                        string displayText = "";
                        try
                        {
                            // Try to get the formatted display text
                            var formatMethod = panel.GetType().GetMethod("Format", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (formatMethod != null)
                            {
                                displayText = formatMethod.Invoke(panel, null) as string ?? "";
                            }
                        }
                        catch (Exception ex)
                        {
                            LogError("GetPanelContent", $"Error getting formatted text: {ex.Message}");
                        }

                        result = new JObject 
                        { 
                            ["status"] = "success", 
                            ["result"] = new JObject
                            {
                                ["user_text"] = userText,
                                ["runtime_content"] = runtimeContent,
                                ["display_text"] = displayText,
                                ["has_runtime_data"] = runtimeContent.Count > 0,
                                ["instance_guid"] = guid,
                                ["nickname"] = panel.NickName,
                                ["properties"] = new JObject
                                {
                                    ["wrap"] = panel.Properties.Wrap,
                                    ["multiline"] = panel.Properties.Multiline,
                                    ["special_codes"] = panel.Properties.SpecialCodes,
                                    ["draw_paths"] = panel.Properties.DrawPaths,
                                    ["draw_indices"] = panel.Properties.DrawIndices,
                                    ["stream_contents"] = panel.Properties.StreamContents,
                                    ["stream_path"] = panel.Properties.StreamPath,
                                    ["alignment"] = panel.Properties.Alignment.ToString(),
                                    ["colour"] = panel.Properties.Colour.ToArgb(),
                                    ["colour_hex"] = "#" + panel.Properties.Colour.ToArgb().ToString("X8")
                                }
                            }
                        };
                    }
                    else
                    {
                        result = new JObject { ["status"] = "error", ["result"] = "Object is not a Panel component." };
                    }
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    done.Set();
                }
            });

            done.Wait(5000);
            if (error != null) return ErrorResponse($"Error getting panel content: {error.Message}");
            return result ?? ErrorResponse("Operation timed out");
        }

        // ================================================================
        // v0.1.1 additions — set_slider_range, get_runtime_messages, capture_canvas
        // ================================================================

        /// <summary>
        /// Adjust the min/max range of an existing Number Slider, clamping the
        /// current value if necessary. The Python tool gh_set_slider_range hits this.
        /// </summary>
        private JObject SetSliderRange(JObject cmd)
        {
            string guid = (string)cmd["instance_guid"];
            double min = (double?)cmd["min_value"] ?? 0.0;
            double max = (double?)cmd["max_value"] ?? 1.0;
            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);

            RunOnUiThread(() =>
            {
                try
                {
                    if (string.IsNullOrEmpty(guid))
                    {
                        result = new JObject { ["status"] = "error", ["result"] = "instance_guid is required." };
                        return;
                    }
                    if (max <= min)
                    {
                        result = new JObject { ["status"] = "error", ["result"] = $"max_value ({max}) must be greater than min_value ({min})." };
                        return;
                    }
                    var doc = GetGHDocument();
                    var obj = doc.FindObject(new Guid(guid), true);
                    if (!(obj is GH_NumberSlider slider))
                    {
                        result = new JObject { ["status"] = "error", ["result"] = "Object is not a Number Slider." };
                        return;
                    }
                    slider.Slider.Minimum = (decimal)min;
                    slider.Slider.Maximum = (decimal)max;
                    // Clamp current value into the new range
                    if (slider.Slider.Value < slider.Slider.Minimum) slider.Slider.Value = slider.Slider.Minimum;
                    if (slider.Slider.Value > slider.Slider.Maximum) slider.Slider.Value = slider.Slider.Maximum;
                    slider.ExpireSolution(false);
                    result = new JObject
                    {
                        ["status"] = "success",
                        ["result"] = new JObject
                        {
                            ["instance_guid"] = guid,
                            ["min"] = (double)slider.Slider.Minimum,
                            ["max"] = (double)slider.Slider.Maximum,
                            ["value"] = (double)slider.Slider.Value
                        }
                    };
                }
                catch (Exception ex) { error = ex; }
                finally { done.Set(); }
            });

            done.Wait(5000);
            if (error != null) return ErrorResponse($"Error setting slider range: {error.Message}");
            return result ?? ErrorResponse("Operation timed out");
        }

        /// <summary>
        /// First-class runtime-messages read. Returns warnings and errors emitted
        /// by a component's most recent solution. The Python tool
        /// gh_get_runtime_messages hits this rather than fishing them out of
        /// get_objects payloads.
        /// </summary>
        private JObject GetRuntimeMessages(JObject cmd)
        {
            string guid = (string)cmd["instance_guid"];
            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);

            RunOnUiThread(() =>
            {
                try
                {
                    if (string.IsNullOrEmpty(guid))
                    {
                        result = new JObject { ["status"] = "error", ["result"] = "instance_guid is required." };
                        return;
                    }
                    var doc = GetGHDocument();
                    var obj = doc.FindObject(new Guid(guid), true) as IGH_ActiveObject;
                    if (obj == null)
                    {
                        result = new JObject { ["status"] = "error", ["result"] = "Object not found or not active." };
                        return;
                    }
                    var messages = new JArray();
                    foreach (GH_RuntimeMessageLevel level in new[] {
                        GH_RuntimeMessageLevel.Error,
                        GH_RuntimeMessageLevel.Warning,
                        GH_RuntimeMessageLevel.Remark,
                    })
                    {
                        foreach (var msg in obj.RuntimeMessages(level))
                        {
                            messages.Add(new JObject
                            {
                                ["level"] = level.ToString(),
                                ["message"] = msg
                            });
                        }
                    }
                    result = new JObject
                    {
                        ["status"] = "success",
                        ["result"] = new JObject
                        {
                            ["instance_guid"] = guid,
                            ["nickname"] = (obj as IGH_DocumentObject)?.NickName ?? "",
                            ["runtime_messages"] = messages
                        }
                    };
                }
                catch (Exception ex) { error = ex; }
                finally { done.Set(); }
            });

            done.Wait(5000);
            if (error != null) return ErrorResponse($"Error getting runtime messages: {error.Message}");
            return result ?? ErrorResponse("Operation timed out");
        }

        /// <summary>
        /// Render the Grasshopper canvas to a PNG, base64-encoded. Lets the
        /// multimodal LLM "see" the current topology.
        ///
        /// Uses GH_Canvas.GenerateHiResImage which is the supported API for
        /// rendering the canvas at higher than screen resolution. Falls back
        /// to a clear error if the canvas is empty or the API misbehaves.
        /// </summary>
        private JObject CaptureCanvas(JObject cmd)
        {
            int maxSize = (int?)(cmd["max_size"] ?? 1200) ?? 1200;
            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);

            RunOnUiThread(() =>
            {
                try
                {
                    var canvas = Grasshopper.Instances.ActiveCanvas;
                    if (canvas == null || canvas.Document == null)
                    {
                        result = new JObject { ["status"] = "error", ["result"] = "No active Grasshopper canvas." };
                        return;
                    }
                    var bbox = canvas.Document.BoundingBox(false);
                    if (bbox.Width < 1 || bbox.Height < 1)
                    {
                        result = new JObject { ["status"] = "error", ["result"] = "Canvas is empty — nothing to capture." };
                        return;
                    }
                    bbox.Inflate(20, 20); // padding for nicer framing

                    // GH_Canvas.GenerateHiResImage(RectangleF region, int dpi) — return a Bitmap.
                    // Try via reflection so we don't hard-fail if the signature shifts between GH minor versions.
                    System.Drawing.Bitmap bmp = null;
                    var canvasType = canvas.GetType();
                    var method = canvasType.GetMethod(
                        "GenerateHiResImage",
                        new[] { typeof(System.Drawing.RectangleF), typeof(int) }
                    );
                    if (method != null)
                    {
                        bmp = method.Invoke(canvas, new object[] { bbox, 96 }) as System.Drawing.Bitmap;
                    }
                    else
                    {
                        var method2 = canvasType.GetMethod(
                            "GenerateHiResImage",
                            new[] { typeof(System.Drawing.RectangleF) }
                        );
                        if (method2 != null)
                            bmp = method2.Invoke(canvas, new object[] { bbox }) as System.Drawing.Bitmap;
                    }
                    if (bmp == null)
                    {
                        result = new JObject { ["status"] = "error", ["result"] = "GH_Canvas.GenerateHiResImage not available in this Grasshopper build." };
                        return;
                    }

                    int origW = bmp.Width, origH = bmp.Height;
                    using (bmp)
                    using (var ms = new System.IO.MemoryStream())
                    {
                        if (bmp.Width > maxSize || bmp.Height > maxSize)
                        {
                            double scale = (double)maxSize / Math.Max(bmp.Width, bmp.Height);
                            int w = Math.Max(1, (int)(bmp.Width * scale));
                            int h = Math.Max(1, (int)(bmp.Height * scale));
                            using (var scaled = new System.Drawing.Bitmap(bmp, w, h))
                                scaled.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        }
                        else
                        {
                            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        }
                        string b64 = Convert.ToBase64String(ms.ToArray());
                        result = new JObject
                        {
                            ["status"] = "success",
                            ["result"] = new JObject
                            {
                                ["data"] = b64,
                                ["format"] = "png",
                                ["original_width"] = origW,
                                ["original_height"] = origH
                            }
                        };
                    }
                }
                catch (Exception ex) { error = ex; }
                finally { done.Set(); }
            });

            done.Wait(15000); // canvas rendering can take a moment on large definitions
            if (error != null) return ErrorResponse($"Error capturing canvas: {error.Message}");
            return result ?? ErrorResponse("Operation timed out");
        }
    }
}