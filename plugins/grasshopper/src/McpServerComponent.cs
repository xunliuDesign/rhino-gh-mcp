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
using System.Globalization;
using System.Reflection;
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
                string ver = name.Version != null
                    ? TrimTrailingZeroParts(name.Version.ToString())
                    : "0.0.0";
                return string.Format("rhino-gh-mcp v{0} ({1})",
                    ver, "https://github.com/xunliuDesign/rhino-gh-mcp");
            }
            catch
            {
                return "rhino-gh-mcp v? (https://github.com/xunliuDesign/rhino-gh-mcp)";
            }
        }

        // .NET assembly versions are always 4 parts (e.g. "0.2.0.0").
        // Display the human semver form by trimming trailing ".0" parts,
        // keeping at least major.minor.
        private static string TrimTrailingZeroParts(string ver)
        {
            if (string.IsNullOrEmpty(ver)) return ver;
            var parts = ver.Split('.');
            int last = parts.Length - 1;
            while (last > 1 && parts[last] == "0") last--;
            return string.Join(".", parts, 0, last + 1);
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
        // v0.2: visibility relaxed to `protected` so McpServerComponentV2 can
        // inherit this class for its TCP listener + dispatch infrastructure
        // without us duplicating ~2k lines of bridge code. V2 only overrides
        // RegisterInputParams + SolveInstance + adds a Scenario knob; the
        // rest of the protocol stays identical.
        protected Thread serverThread = null;
        protected volatile bool runServer = false;
        protected string serverStatus = "Server Off";
        protected string debugOutput = "";
        protected int port = 9999;
        protected string host = "127.0.0.1";
        protected string lastError = null;
        protected TcpListener listener = null;
        protected object serverLock = new object();
        protected string currentCategoryFilter = "MCP";

        // v0.1.5: capability + scope state (settable from canvas inputs).
        protected bool currentAllowParameters = true;
        protected bool currentAllowComponents = true;
        protected bool currentAllowScripting = false;
        // 0 = curated (CategoryFilter), 1 = gh defaults, 2 = all
        protected int currentComponentScope = 1;

        protected bool currentAutoRecompute = false;

        // v0.2: scenario + active-skill state, set by V2 component's SolveInstance.
        // V1 leaves these at defaults — the bridge surfaces them in get_capabilities
        // so the Python server can derive its own gating decisions. Static so V2
        // (a separate component instance) and V1 share the same authoritative state
        // when both happen to be on the same canvas (edge case — only one bridge
        // listener can hold the port at a time, but the fields are read elsewhere).
        protected static string currentScenario = "author"; // inspect|tune|coach|execute|author
        protected static string currentActiveSkill = "";    // empty = no skill restriction

        // === Persistent Debug Log and Error System ===
        // v0.2: protected so subclasses (V2) can log into the same buffer.
        protected static ConcurrentQueue<string> debugLog = new ConcurrentQueue<string>();
        protected void LogDebug(string msg) { debugLog.Enqueue($"[{DateTime.Now:HH:mm:ss}] {msg}"); if (debugLog.Count > 1000) debugLog.TryDequeue(out _); }
        protected void LogError(string msg) { lastError = $"[{DateTime.Now:HH:mm:ss}] {msg}"; LogDebug("ERROR: " + msg); }
        protected void LogError(string context, string msg) { lastError = $"[{DateTime.Now:HH:mm:ss}] [{context}] {msg}"; LogDebug($"ERROR [{context}]: {msg}"); }
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
        // v0.2: protected so V2 component can reuse the listener loop verbatim.
        protected void ServerThreadLoop()
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
                    case "set_expression_formula":
                        result = SetExpressionFormula(cmd);
                        break;
                    // v0.1.5: expose current capability state to the Python server.
                    case "get_capabilities":
                        result = GetCapabilities(cmd);
                        break;
                    // v0.1.7 diagnostic: introspect a component's runtime type
                    // surface. Gated on AllowScripting via _scriptingCommands.
                    case "inspect_type":
                        result = InspectType(cmd);
                        break;
                    case "read_script_source":
                        result = ReadScriptSource(cmd);
                        break;
                    // v0.2 additions ----------------------------------------
                    // Turn tracking — Coach mode infrastructure. The Python
                    // server brackets each AI response with begin/end_turn so
                    // canvas-side highlighting (deferred to v0.2.x) can render
                    // "what changed this turn" badges. In v0.2.0 the handlers
                    // record state but do not paint anything.
                    case "begin_turn":
                        result = BeginTurn(cmd);
                        break;
                    case "end_turn":
                        result = EndTurn(cmd);
                        break;
                    case "dismiss_highlights":
                        result = DismissHighlights(cmd);
                        break;
                    // Skill reference file loading — places a saved .gh
                    // definition's components onto the current canvas at a
                    // chosen pivot. Gated as a component-write op.
                    case "load_definition":
                        result = LoadDefinitionFromBase64(cmd);
                        break;
                    // v0.2.3 productivity tools.
                    case "bake_to_rhino":
                        result = BakeToRhino(cmd);
                        break;
                    case "reference_rhino_object":
                        result = ReferenceRhinoObject(cmd);
                        break;
                    case "add_panel":
                        result = AddPanel(cmd);
                        break;
                    case "set_panel_content":
                        result = SetPanelContent(cmd);
                        break;
                    case "get_component_output":
                        result = GetComponentOutput(cmd);
                        break;
                    case "group_components":
                        result = GroupComponents(cmd);
                        break;
                    case "move_component":
                        result = MoveComponent(cmd);
                        break;
                    case "organize_components":
                        result = OrganizeComponents(cmd);
                        break;
                    // v0.2.4: ultra-compact outline tools for fast canvas
                    // analysis without dumping wire-level JSON.
                    case "canvas_outline":
                        result = CanvasOutline(cmd);
                        break;
                    case "file_outline":
                        result = FileOutlineFromBase64(cmd);
                        break;
                    case "cluster_flow":
                        result = ClusterFlow(cmd);
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
            // Bug 13 fix: shared cancel flag so that if the wait below times out,
            // a late-running UI callback can detect it and roll back the placement
            // instead of leaving a duplicate component on the canvas.
            bool canceled = false;
            object cancelLock = new object();
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

                    // v0.2.5 fix: prefer non-obsolete proxies. GH keeps
                    // deprecated component versions around as `Obsolete=true`
                    // for backwards compat (e.g. there's both a current
                    // "Circle" and an "Old" Circle). FirstOrDefault would pick
                    // whichever came first in the enumeration, which depends
                    // on assembly load order. Filter on Obsolete and fall back
                    // only if the only available match is obsolete.
                    var matching = server.ObjectProxies.Where(p =>
                        p?.Desc != null &&
                        scopePredicate(p) &&
                        (p.Desc.Name == name || p.Desc.NickName == name)
                    ).ToList();
                    var proxy = matching.FirstOrDefault(p => !p.Obsolete)
                                ?? matching.FirstOrDefault();
                    if (proxy == null)
                    {
                        string scopeDesc = effectiveScope == 2 ? "ANY scope"
                                         : effectiveScope == 1 ? "Grasshopper-default scope"
                                         : $"curated scope (categories: {currentCategoryFilter})";
                        result = new JObject { ["status"] = "error", ["result"] = $"Component '{name}' not found in {scopeDesc}." };
                    }
                    else
                    {
                        // Bug 13 fix: if the caller has already timed out (canceled=true),
                        // skip the placement entirely so we don't leave a phantom component.
                        lock (cancelLock) { if (canceled) return; }
                        var comp = proxy.CreateInstance();
                        comp.CreateAttributes();
                        comp.Attributes.Pivot = new System.Drawing.PointF(x, y);
                        doc.AddObject(comp, false);
                        // Re-check after AddObject: if the wait timed out while we were
                        // placing, roll back so the caller's "Operation timed out" return
                        // matches reality on the canvas.
                        bool rolledBack = false;
                        lock (cancelLock)
                        {
                            if (canceled)
                            {
                                doc.RemoveObject(comp, false);
                                rolledBack = true;
                            }
                        }
                        if (rolledBack) return;
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

            // Bug 13 fix: 15s gives proxy enumeration (~thousands of entries) headroom
            // on slow machines; pair with the rollback above so a real timeout doesn't
            // leak a placed component.
            bool gotResponse = done.Wait(15000);
            if (!gotResponse)
            {
                lock (cancelLock) { canceled = true; }
                return ErrorResponse("Operation timed out (15s). The placement was canceled; canvas should be unchanged. Call gh_find_components before retrying to be safe.");
            }
            if (error != null) return ErrorResponse($"Error adding component: {error.Message}");
            return result ?? ErrorResponse("Operation completed without result");
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
                    RecordTurnChange(slider.InstanceGuid);  // v0.2.4 bug fix
                    result = new JObject { ["status"] = "success", ["result"] = $"Slider '{name}' added at ({x},{y})", ["instance_guid"] = slider.InstanceGuid.ToString() };
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
                        else if (obj is IGH_DocumentObject docObj)
                            all[docObj.InstanceGuid.ToString()] = GetGenericObjectInfo(docObj);
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
                        else if (obj is IGH_DocumentObject docObj && set.Contains(docObj.InstanceGuid.ToString()))
                            all[docObj.InstanceGuid.ToString()] = GetGenericObjectInfo(docObj);
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
                            else if (obj is IGH_DocumentObject docObj)
                                all[docObj.InstanceGuid.ToString()] = GetGenericObjectInfo(docObj);
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
                    if (obj == null)
                    {
                        result = new JObject { ["status"] = "error", ["result"] = "Component not found." };
                        return;
                    }

                    // Resolve where the script source lives across component generations:
                    //   - Rhino 6/7 GhPython:          public writable string property "Code".
                    //   - Rhino 8 Python3/IronPython2: RhinoCodePlatform.GH.IScriptComponent.Text,
                    //     an EXPLICIT interface implementation -> invisible to GetProperty("Code")
                    //     or GetProperty("Text"); it must be reached through the interface map.
                    // The old code only looked for "Code", so on Rhino 8 GetProperty("Code")
                    // returned null and SetValue null-ref'd ("Object reference not set...").
                    var codeProp = obj.GetType().GetProperty("Code");
                    bool hasLegacyCode = codeProp != null && codeProp.CanWrite;
                    PropertyInfo textProp = null;
                    if (!hasLegacyCode)
                    {
                        var scriptIface = obj.GetType().GetInterface("IScriptComponent");
                        if (scriptIface != null) textProp = scriptIface.GetProperty("Text");
                    }

                    if (!hasLegacyCode && (textProp == null || !textProp.CanWrite))
                    {
                        result = new JObject { ["status"] = "error", ["result"] = "Not a script component (no writable Code or IScriptComponent.Text)." };
                        return;
                    }

                    if (paramDefs != null) { /* param update logic omitted for brevity */ }

                    if (code != null)
                    {
                        if (hasLegacyCode) codeProp.SetValue(obj, code, null);
                        else textProp.SetValue(obj, code, null);
                    }

                    if (desc != null)
                    {
                        try { obj.Description = desc; }
                        catch
                        {
                            var descProp = obj.GetType().GetInterface("IScriptComponent")?.GetProperty("Description");
                            if (descProp != null && descProp.CanWrite) descProp.SetValue(obj, desc, null);
                        }
                    }

                    obj.ExpireSolution(true);
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
            // Bug 8 fix: when true, add this wire alongside existing sources instead
            // of replacing them. Lets callers build multi-source merges (e.g. Loft
            // from Project+Contour) without inserting an explicit Merge component.
            bool append = (bool?)(cmd["append"] ?? false) ?? false;
            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);

            RunOnUiThread(() =>
            {
                try
                {
                    var doc = GetGHDocument();
                    var src = doc.FindObject(new Guid(srcGuid), true);
                    var tgt = doc.FindObject(new Guid(tgtGuid), true);
                    if (src == null || tgt == null)
                    {
                        result = new JObject { ["status"] = "error", ["result"] = "Source or target not found." };
                        return;
                    }
                    IGH_Param srcParam = null;
                    if (src is GH_NumberSlider) srcParam = src as IGH_Param;
                    else if (src is IGH_Component sc) srcParam = sc.Params.Output.FirstOrDefault(p => p.NickName == srcOut || p.Name == srcOut);
                    else if (src is IGH_Param ip) srcParam = ip;
                    IGH_Param tgtParam = null;
                    if (tgt is IGH_Component tc) tgtParam = tc.Params.Input.FirstOrDefault(p => p.NickName == tgtIn || p.Name == tgtIn);
                    else if (tgt is IGH_Param tp) tgtParam = tp;
                    if (srcParam == null || tgtParam == null)
                    {
                        result = new JObject { ["status"] = "error", ["result"] = "Params not found." };
                        return;
                    }
                    // Bug 8 fix: when append=true, keep existing wires so this AddSource
                    // adds to the merge instead of replacing. When false (default),
                    // preserve the original replace-only semantics.
                    if (!append)
                    {
                        while (tgtParam.Sources.Count > 0) tgtParam.RemoveSource(tgtParam.Sources[0]);
                    }
                    // Guard against accidentally creating a duplicate wire when the same
                    // (source, target) pair is appended twice.
                    if (!tgtParam.Sources.Contains(srcParam))
                    {
                        tgtParam.AddSource(srcParam);
                    }
                    result = new JObject
                    {
                        ["status"] = "success",
                        ["result"] = new JObject
                        {
                            ["message"] = "Connected.",
                            ["append"] = append,
                            ["source_count"] = tgtParam.Sources.Count
                        }
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
                                    ["Obsolete"] = proxy.Obsolete,  // v0.2.5: flag deprecated proxies
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
        // Bug 27 fix: serialize canvas objects that aren't IGH_Component or
        // top-level IGH_Param (Galapagos solver, Gene Pool, other
        // GH_ActiveObject-only classes). Without this, those objects are
        // invisible to gh_get_context / gh_canvas_summary even though the
        // user can see them on the canvas.
        private JObject GetGenericObjectInfo(IGH_DocumentObject obj)
        {
            var o = new JObject();
            o["instanceGuid"] = obj.InstanceGuid.ToString();
            o["name"] = obj.Name ?? "";
            o["nickName"] = obj.NickName ?? "";
            o["description"] = obj.Description ?? "";
            o["category"] = obj.Category ?? "";
            o["subCategory"] = obj.SubCategory ?? "";
            o["kind"] = obj.GetType().Name;
            o["isSelected"] = obj.Attributes?.Selected ?? false;
            o["isInput"] = false;
            o["isOutput"] = false;
            return o;
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
        // v0.2: protected so V2 component can call from its own thread loop.
        protected void ExpireComponentSolution()
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
                    // Bug 2 fix: when the target is a Number Slider, move the slider
                    // itself rather than wiring an orphan panel into its (empty) source list.
                    // Mirrors the SetSliderRange pattern: clamp to range, ExpireSolution.
                    if (obj is GH_NumberSlider slider)
                    {
                        if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                        {
                            result = new JObject { ["status"] = "error", ["result"] = $"Value '{value}' is not a valid number for a Number Slider." };
                            return;
                        }
                        if (parsed < slider.Slider.Minimum) parsed = slider.Slider.Minimum;
                        if (parsed > slider.Slider.Maximum) parsed = slider.Slider.Maximum;
                        slider.Slider.Value = parsed;
                        slider.ExpireSolution(false);
                        result = new JObject
                        {
                            ["status"] = "success",
                            ["result"] = new JObject
                            {
                                ["instance_guid"] = guid,
                                ["value"] = (double)slider.Slider.Value,
                                ["mode"] = "slider"
                            }
                        };
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
                    // Bug 7 fix: for typed inputs, write the value to PersistentData
                    // directly instead of wiring an orphan panel. Unhandled types
                    // (Param_GenericObject, Param_Point, etc.) and unparseable values
                    // fall through to the panel-mode block below.
                    string typedMode = null;
                    switch (param)
                    {
                        case Param_Integer pi:
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intVal))
                            {
                                param.RemoveAllSources();
                                pi.PersistentData.Clear();
                                pi.PersistentData.Append(new GH_Integer(intVal));
                                typedMode = "integer";
                            }
                            break;
                        case Param_Number pn:
                            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var dblVal))
                            {
                                param.RemoveAllSources();
                                pn.PersistentData.Clear();
                                pn.PersistentData.Append(new GH_Number(dblVal));
                                typedMode = "number";
                            }
                            break;
                        case Param_Boolean pb:
                            if (TryParseBool(value, out var boolVal))
                            {
                                param.RemoveAllSources();
                                pb.PersistentData.Clear();
                                pb.PersistentData.Append(new GH_Boolean(boolVal));
                                typedMode = "boolean";
                            }
                            break;
                        case Param_String ps:
                            param.RemoveAllSources();
                            ps.PersistentData.Clear();
                            ps.PersistentData.Append(new GH_String(value));
                            typedMode = "string";
                            break;
                        case Param_Interval piv:
                            if (TryParseInterval(value, out var iv))
                            {
                                param.RemoveAllSources();
                                piv.PersistentData.Clear();
                                piv.PersistentData.Append(new GH_Interval(iv));
                                typedMode = "interval";
                            }
                            break;
                    }
                    if (typedMode != null)
                    {
                        param.ExpireSolution(false);
                        result = new JObject
                        {
                            ["status"] = "success",
                            ["result"] = new JObject
                            {
                                ["instance_guid"] = guid,
                                ["param_name"] = paramName,
                                ["value"] = value,
                                ["mode"] = typedMode
                            }
                        };
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

        // Bug 7 fix helpers — parse boolean / interval strings sent over the bridge.
        private static bool TryParseBool(string raw, out bool result)
        {
            result = false;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var s = raw.Trim();
            if (bool.TryParse(s, out result)) return true;
            if (s.Equals("1") || s.Equals("yes", StringComparison.OrdinalIgnoreCase) || s.Equals("on", StringComparison.OrdinalIgnoreCase)) { result = true; return true; }
            if (s.Equals("0") || s.Equals("no", StringComparison.OrdinalIgnoreCase) || s.Equals("off", StringComparison.OrdinalIgnoreCase)) { result = false; return true; }
            return false;
        }

        private static readonly string[] _intervalSeparators = new[] { " to ", "..", ",", ";", ":" };

        private static bool TryParseInterval(string raw, out Interval result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var s = raw.Trim();
            // Try "a SEP b" forms first.
            foreach (var sep in _intervalSeparators)
            {
                var idx = s.IndexOf(sep, StringComparison.OrdinalIgnoreCase);
                if (idx <= 0) continue;
                var left = s.Substring(0, idx).Trim();
                var right = s.Substring(idx + sep.Length).Trim();
                if (double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var a) &&
                    double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
                {
                    result = new Interval(a, b);
                    return true;
                }
            }
            // Single number → 0 to N (matches GH's implicit Number→Domain cast).
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
            {
                result = new Interval(0.0, n);
                return true;
            }
            return false;
        }

        private JObject ExecuteCode(JObject cmd)
        {
            // Not supported in C# context
            return ErrorResponse("execute_code is not supported in C# MCP server.");
        }

        // v0.1.7 diagnostic: dump the runtime type surface of a canvas object so
        // we can discover the property names used by third-party / Rhino-8
        // components (e.g. RhinoCodePluginGH.Components.ScriptComponent) without
        // bundling reference assemblies.
        private JObject InspectType(JObject cmd)
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
                    var obj = doc.FindObject(new Guid(guid), false);
                    if (obj == null)
                    {
                        result = ErrorResponse($"No object with guid {guid}");
                        return;
                    }
                    var t = obj.GetType();
                    var props = new JArray();
                    foreach (var p in t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    {
                        var entry = new JObject
                        {
                            ["name"] = p.Name,
                            ["type"] = p.PropertyType.FullName,
                            ["canRead"] = p.CanRead,
                            ["canWrite"] = p.CanWrite,
                        };
                        if (p.CanRead && p.GetIndexParameters().Length == 0)
                        {
                            try
                            {
                                var val = p.GetValue(obj, null);
                                if (val == null) entry["value"] = null;
                                else if (val is string || val is bool || val is int || val is long || val is double || val is float)
                                {
                                    string s = val.ToString();
                                    entry["value"] = s.Length > 400 ? s.Substring(0, 400) + "..." : s;
                                }
                                else entry["valueType"] = val.GetType().FullName;
                            }
                            catch (Exception ex) { entry["readError"] = ex.GetType().Name + ": " + ex.Message; }
                        }
                        props.Add(entry);
                    }
                    var fields = new JArray();
                    string probeFieldName = (string)cmd["probe_field"];
                    JObject probeFieldResult = null;
                    foreach (var f in t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                                       .Concat(t.BaseType?.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) ?? System.Linq.Enumerable.Empty<System.Reflection.FieldInfo>()))
                    {
                        var fEntry = new JObject
                        {
                            ["name"] = f.Name,
                            ["type"] = f.FieldType.FullName,
                            ["isPublic"] = f.IsPublic,
                            ["declaredOn"] = f.DeclaringType?.FullName,
                        };
                        try
                        {
                            var fval = f.GetValue(obj);
                            if (fval == null) fEntry["value"] = null;
                            else if (fval is string || fval is bool || fval is int || fval is long || fval is double || fval is float || fval.GetType().IsEnum)
                            {
                                string s = fval.ToString();
                                fEntry["value"] = s.Length > 400 ? s.Substring(0, 400) + "..." : s;
                            }
                            else fEntry["valueType"] = fval.GetType().FullName;

                            if (probeFieldName != null && f.Name == probeFieldName && fval != null)
                            {
                                var ft = fval.GetType();
                                var fprops = new JArray();
                                foreach (var pp in ft.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                                {
                                    var pe = new JObject
                                    {
                                        ["name"] = pp.Name,
                                        ["type"] = pp.PropertyType.FullName,
                                        ["canWrite"] = pp.CanWrite,
                                    };
                                    if (pp.CanRead && pp.GetIndexParameters().Length == 0)
                                    {
                                        try
                                        {
                                            var pv = pp.GetValue(fval, null);
                                            if (pv == null) pe["value"] = null;
                                            else if (pv is string || pv is bool || pv is int || pv is double || pv.GetType().IsEnum)
                                            {
                                                string s = pv.ToString();
                                                pe["value"] = s.Length > 400 ? s.Substring(0, 400) + "..." : s;
                                            }
                                            else pe["valueType"] = pv.GetType().FullName;
                                        }
                                        catch (Exception ex) { pe["readError"] = ex.Message; }
                                    }
                                    fprops.Add(pe);
                                }
                                var fmethods = new JArray();
                                foreach (var m in ft.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                                {
                                    if (m.IsSpecialName) continue;
                                    var ps = new JArray();
                                    foreach (var par in m.GetParameters()) ps.Add(par.ParameterType.FullName + " " + par.Name);
                                    fmethods.Add(new JObject
                                    {
                                        ["name"] = m.Name,
                                        ["returns"] = m.ReturnType.FullName,
                                        ["params"] = ps,
                                    });
                                }
                                probeFieldResult = new JObject
                                {
                                    ["type"] = ft.FullName,
                                    ["assembly"] = ft.Assembly.GetName().Name,
                                    ["baseType"] = ft.BaseType?.FullName,
                                    ["interfaces"] = new JArray(ft.GetInterfaces().Select(i => (JToken)i.FullName)),
                                    ["properties"] = fprops,
                                    ["methods"] = fmethods,
                                };
                            }
                        }
                        catch (Exception ex) { fEntry["readError"] = ex.Message; }
                        fields.Add(fEntry);
                    }
                    var methods = new JArray();
                    foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    {
                        if (m.IsSpecialName) continue;
                        var n = m.Name;
                        // keep methods whose name suggests script/code/language manipulation, plus interface-implementations
                        if (!(n.IndexOf("Code", StringComparison.OrdinalIgnoreCase) >= 0
                              || n.IndexOf("Source", StringComparison.OrdinalIgnoreCase) >= 0
                              || n.IndexOf("Script", StringComparison.OrdinalIgnoreCase) >= 0
                              || n.IndexOf("Language", StringComparison.OrdinalIgnoreCase) >= 0
                              || n.IndexOf("Spec", StringComparison.OrdinalIgnoreCase) >= 0
                              || n.IndexOf("Text", StringComparison.OrdinalIgnoreCase) >= 0
                              || n.IndexOf("Set", StringComparison.OrdinalIgnoreCase) >= 0))
                            continue;
                        var ps = new JArray();
                        foreach (var par in m.GetParameters()) ps.Add(par.ParameterType.FullName + " " + par.Name);
                        methods.Add(new JObject
                        {
                            ["name"] = m.Name,
                            ["returns"] = m.ReturnType.FullName,
                            ["params"] = ps,
                            ["declaredOn"] = m.DeclaringType?.FullName,
                        });
                    }
                    var interfaces = new JArray();
                    foreach (var i in t.GetInterfaces()) interfaces.Add(i.FullName);
                    result = new JObject
                    {
                        ["status"] = "success",
                        ["result"] = new JObject
                        {
                            ["typeFullName"] = t.FullName,
                            ["assembly"] = t.Assembly.GetName().Name,
                            ["assemblyLocation"] = t.Assembly.Location,
                            ["baseType"] = t.BaseType?.FullName,
                            ["interfaces"] = interfaces,
                            ["properties"] = props,
                            ["fields"] = fields,
                            ["methods_filtered"] = methods,
                            ["probedField"] = probeFieldName,
                            ["probedFieldResult"] = probeFieldResult,
                        }
                    };
                }
                catch (Exception ex) { error = ex; }
                finally { done.Set(); }
            });

            done.Wait(5000);
            if (error != null) return ErrorResponse($"Error in InspectType: {error.Message}");
            return result ?? ErrorResponse("Operation timed out");
        }

        // v0.1.7 diagnostic: read the script source from a Rhino 8
        // ScriptComponent by reflection. Calls BaseScriptComponent<,>.TryGetSource.
        private JObject ReadScriptSource(JObject cmd)
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
                    var obj = doc.FindObject(new Guid(guid), false);
                    if (obj == null) { result = ErrorResponse($"No object with guid {guid}"); return; }
                    var t = obj.GetType();
                    var tryGet = FindMethod(t, "TryGetSource");
                    var getSourceCode = FindMethod(t, "GetSourceCode");
                    object[] args;
                    string source = null;
                    bool ok = false;
                    string via = null;
                    if (tryGet != null)
                    {
                        args = new object[] { null };
                        var rv = tryGet.Invoke(obj, args);
                        ok = rv is bool b && b;
                        source = args[0] as string;
                        via = "TryGetSource";
                    }
                    else if (getSourceCode != null)
                    {
                        source = getSourceCode.Invoke(obj, null) as string;
                        ok = source != null;
                        via = "GetSourceCode";
                    }
                    // also dump Context type/properties for visibility
                    var ctxField = t.GetField("Context", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                                ?? t.BaseType?.GetField("Context", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var ctx = ctxField?.GetValue(obj);
                    JObject ctxInfo = null;
                    if (ctx != null)
                    {
                        var ct = ctx.GetType();
                        var cprops = new JArray();
                        foreach (var p in ct.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                        {
                            string val = null;
                            try
                            {
                                var v = p.GetValue(ctx, null);
                                if (v == null) val = "<null>";
                                else if (v is string || v is bool || v is int || v is double || v.GetType().IsEnum) val = v.ToString();
                                else val = "<" + v.GetType().FullName + ">";
                            }
                            catch (Exception ex) { val = "<err: " + ex.Message + ">"; }
                            cprops.Add(new JObject
                            {
                                ["name"] = p.Name,
                                ["type"] = p.PropertyType.FullName,
                                ["canWrite"] = p.CanWrite,
                                ["value"] = val,
                            });
                        }
                        var cfields = new JArray();
                        foreach (var f in ct.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic))
                        {
                            string val = null;
                            try
                            {
                                var v = f.GetValue(ctx);
                                if (v == null) val = "<null>";
                                else if (v is string || v is bool || v is int || v is double || v.GetType().IsEnum) val = v.ToString();
                                else val = "<" + v.GetType().FullName + ">";
                            }
                            catch (Exception ex) { val = "<err: " + ex.Message + ">"; }
                            cfields.Add(new JObject { ["name"] = f.Name, ["type"] = f.FieldType.FullName, ["value"] = val });
                        }
                        ctxInfo = new JObject
                        {
                            ["type"] = ct.FullName,
                            ["properties"] = cprops,
                            ["fields"] = cfields,
                        };
                    }
                    result = new JObject
                    {
                        ["status"] = "success",
                        ["result"] = new JObject
                        {
                            ["readVia"] = via,
                            ["ok"] = ok,
                            ["source"] = source,
                            ["context"] = ctxInfo,
                        }
                    };
                }
                catch (Exception ex) { error = ex; }
                finally { done.Set(); }
            });
            done.Wait(5000);
            if (error != null) return ErrorResponse($"Error in ReadScriptSource: {error.Message}");
            return result ?? ErrorResponse("Operation timed out");
        }

        private static System.Reflection.MethodInfo FindMethod(Type t, string name)
        {
            for (var ty = t; ty != null; ty = ty.BaseType)
            {
                var m = ty.GetMethod(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
                if (m != null) return m;
            }
            return null;
        }

        // v0.1.5: command-name -> capability-bucket mappings, used by
        // DispatchCommand to short-circuit out-of-capability calls.
        private static readonly HashSet<string> _parameterWriteCommands = new HashSet<string>
        {
            "set_component_parameter",
            "set_slider_range",
            "set_toggle_value",
            "set_value_list_selection",
            "set_expression_formula",
        };
        private static readonly HashSet<string> _componentWriteCommands = new HashSet<string>
        {
            "add_component_to_canvas",
            "add_slider_to_canvas",
            "connect_components",
            "remove_node",
            // v0.2: dropping a Skill's reference .gh onto the canvas mutates
            // the component tree, so it needs AllowComponents.
            "load_definition",
        };
        private static readonly HashSet<string> _scriptingCommands = new HashSet<string>
        {
            "update_script",
            "update_script_with_code_reference",
            "execute_code",
            "inspect_type",
            "read_script_source",
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
        // v0.2: additionally surface `scenario` and `active_skill` so the server
        // can derive its own Coach/Execute-mode behaviour without re-asking the
        // canvas. Old (v0.1) component leaves them at default values; new (v2)
        // component sets them from its Scenario and ActiveSkill inputs.
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
                    ["scenario"] = currentScenario,
                    ["active_skill"] = currentActiveSkill,
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
        // Bug 14 fix — write formula string to Expression / VariableExpression /
        // Evaluate components. These store the formula as a [Property] on the
        // component class, not as an input, so gh_set_component_parameter can't
        // reach it. This handler walks public string properties for a writable
        // "Expression" / "Formula" / "Function" match and sets it via reflection.
        // ================================================================
        private static readonly string[] _expressionFormulaPropertyNames = new[] { "Expression", "Formula", "Function" };

        private JObject SetExpressionFormula(JObject cmd)
        {
            string guid = (string)cmd["instance_guid"];
            string formula = (string)cmd["formula"];
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
                    if (formula == null)
                    {
                        result = new JObject { ["status"] = "error", ["result"] = "formula is required." };
                        return;
                    }
                    var doc = GetGHDocument();
                    var obj = doc.FindObject(new Guid(guid), true);
                    if (obj == null)
                    {
                        result = new JObject { ["status"] = "error", ["result"] = "Object not found." };
                        return;
                    }
                    var type = obj.GetType();
                    PropertyInfo propUsed = null;
                    foreach (var candidate in _expressionFormulaPropertyNames)
                    {
                        var p = type.GetProperty(candidate, BindingFlags.Public | BindingFlags.Instance);
                        if (p != null && p.PropertyType == typeof(string) && p.CanWrite)
                        {
                            p.SetValue(obj, formula);
                            propUsed = p;
                            break;
                        }
                    }
                    if (propUsed == null)
                    {
                        result = new JObject { ["status"] = "error", ["result"] = $"Component '{type.Name}' has no writable string property named Expression/Formula/Function. This tool is for Expression, Variable Expression, and Evaluate components." };
                        return;
                    }
                    if (obj is IGH_ActiveObject ao) ao.ExpireSolution(false);
                    result = new JObject
                    {
                        ["status"] = "success",
                        ["result"] = new JObject
                        {
                            ["instance_guid"] = guid,
                            ["formula"] = formula,
                            ["property"] = propUsed.Name,
                            ["component"] = type.Name
                        }
                    };
                }
                catch (Exception ex) { error = ex; }
                finally { done.Set(); }
            });

            done.Wait(5000);
            if (error != null) return ErrorResponse($"Error setting expression formula: {error.Message}");
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

        // ============================================================
        // v0.2 — Coach-mode turn tracking + skill reference loading.
        // ============================================================
        //
        // Turn tracking maps an AI response cycle (one round of begin_turn +
        // bridge calls + end_turn) to the set of canvas GUIDs the AI touched
        // during that cycle. The plan in v0.2.0 is text-only: we record state
        // and surface it via end_turn's response so the AI can self-narrate
        // ("What I changed this turn: ..."). Canvas-side visual highlighting
        // is deferred to v0.2.x (TODO below).

        // Active turn id, monotonic per-process. Long so wrap is impractical.
        private static long currentTurnId = 0;
        private static readonly object turnLock = new object();
        // turnId -> set of GUIDs the AI touched during that turn.
        private static Dictionary<long, HashSet<Guid>> turnChanges =
            new Dictionary<long, HashSet<Guid>>();

        // v0.2.x — canvas-side highlight state. Populated by EndTurn, drained
        // by DismissHighlights. The post-paint handler (OnCanvasPostPaint)
        // draws a teal ring around every object in this set after the canvas
        // finishes its normal render pass.
        private static readonly HashSet<Guid> highlightedGuids = new HashSet<Guid>();
        private static bool canvasHandlerHooked = false;

        // Public so the bridge handlers below — and v0.2.x highlight painter —
        // can append to the active turn from any thread.
        protected static void RecordTurnChange(Guid guid)
        {
            lock (turnLock)
            {
                if (currentTurnId == 0) return;
                if (!turnChanges.TryGetValue(currentTurnId, out var set))
                {
                    set = new HashSet<Guid>();
                    turnChanges[currentTurnId] = set;
                }
                set.Add(guid);
            }
        }

        private JObject BeginTurn(JObject cmd)
        {
            lock (turnLock)
            {
                currentTurnId++;
                if (!turnChanges.ContainsKey(currentTurnId))
                    turnChanges[currentTurnId] = new HashSet<Guid>();
                return SuccessResponse(new JObject
                {
                    ["turn_id"] = currentTurnId,
                });
            }
        }

        private JObject EndTurn(JObject cmd)
        {
            long turnId = (long?)cmd["turn_id"] ?? 0L;
            HashSet<Guid> snapshot;
            lock (turnLock)
            {
                if (turnId == 0 || !turnChanges.TryGetValue(turnId, out var set))
                {
                    return SuccessResponse(new JObject
                    {
                        ["turn_id"] = turnId,
                        ["changed_count"] = 0,
                        ["changed_guids"] = new JArray(),
                    });
                }
                snapshot = new HashSet<Guid>(set);
                // v0.2.x — keep these GUIDs in the canvas highlight set until
                // gh_dismiss_highlights is called. Union (not assign) so multi-turn
                // sessions accumulate the badges instead of overwriting earlier turns.
                highlightedGuids.UnionWith(set);
            }

            // Touch the canvas outside the lock — UI thread + redraw to surface
            // the rings the post-paint handler draws.
            RunOnUiThread(() =>
            {
                EnsureCanvasHandlerHooked();
                Grasshopper.Instances.ActiveCanvas?.Refresh();
            });

            var arr = new JArray();
            foreach (var g in snapshot)
                arr.Add(g.ToString());
            return SuccessResponse(new JObject
            {
                ["turn_id"] = turnId,
                ["changed_count"] = snapshot.Count,
                ["changed_guids"] = arr,
            });
        }

        private JObject DismissHighlights(JObject cmd)
        {
            long? turnId = (long?)cmd["turn_id"];
            JObject response;
            lock (turnLock)
            {
                if (turnId == null || turnId == 0)
                {
                    int n = turnChanges.Count;
                    turnChanges.Clear();
                    highlightedGuids.Clear();
                    response = new JObject { ["cleared_turns"] = n };
                }
                else
                {
                    if (turnChanges.TryGetValue(turnId.Value, out var set) && set != null)
                        highlightedGuids.ExceptWith(set);
                    turnChanges.Remove(turnId.Value);
                    response = new JObject { ["cleared_turn"] = turnId.Value };
                }
            }
            RunOnUiThread(() => Grasshopper.Instances.ActiveCanvas?.Refresh());
            return SuccessResponse(response);
        }

        // v0.2.x — lazily hook the canvas post-paint event so we draw highlights
        // after the default render pass. Idempotent: safe to call from every
        // EndTurn. We do NOT unhook on server stop — the handler is harmless
        // when highlightedGuids is empty and Grasshopper tears it down with the
        // canvas itself.
        private static void EnsureCanvasHandlerHooked()
        {
            if (canvasHandlerHooked) return;
            var canvas = Grasshopper.Instances.ActiveCanvas;
            if (canvas == null) return;
            canvas.CanvasPostPaintObjects -= OnCanvasPostPaint;
            canvas.CanvasPostPaintObjects += OnCanvasPostPaint;
            canvasHandlerHooked = true;
        }

        // Post-paint handler: walk highlightedGuids and outline each object's
        // bounds with a teal ring. The color is deliberately distinct from
        // Grasshopper's built-in selection (white) and runtime-message tints
        // (warning yellow, error red) so the user can tell at a glance that
        // the AI touched these.
        private static void OnCanvasPostPaint(Grasshopper.GUI.Canvas.GH_Canvas sender)
        {
            HashSet<Guid> snapshot;
            lock (turnLock)
            {
                if (highlightedGuids.Count == 0) return;
                snapshot = new HashSet<Guid>(highlightedGuids);
            }
            var doc = sender?.Document;
            if (doc == null) return;
            var g = sender.Graphics;
            if (g == null) return;
            using (var pen = new System.Drawing.Pen(
                System.Drawing.Color.FromArgb(255, 0, 200, 200), 3f))
            {
                pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                foreach (var guid in snapshot)
                {
                    var obj = doc.FindObject(guid, false);
                    if (obj?.Attributes == null) continue;
                    var bounds = System.Drawing.RectangleF.Inflate(
                        obj.Attributes.Bounds, 4f, 4f);
                    g.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width, bounds.Height);
                }
            }
        }

        // Load a .gh definition (sent as base64-encoded bytes) and merge its
        // components into the current canvas at an optional pivot. The Python
        // tool gh_load_skill_reference reads the file off disk and sends it
        // here; doing the file read server-side keeps the bridge protocol
        // string-only and avoids cross-process file-path coupling.
        private JObject LoadDefinitionFromBase64(JObject cmd)
        {
            string b64 = (string)cmd["data"];
            float pivotX = (float?)cmd["pivot_x"] ?? 100f;
            float pivotY = (float?)cmd["pivot_y"] ?? 100f;
            if (string.IsNullOrEmpty(b64))
                return ErrorResponse("`data` is required (base64-encoded .gh archive bytes).");

            byte[] bytes;
            try { bytes = Convert.FromBase64String(b64); }
            catch (Exception ex) { return ErrorResponse($"Bad base64 payload: {ex.Message}"); }

            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);
            RunOnUiThread(() =>
            {
                string tmpPath = null;
                try
                {
                    tmpPath = Path.Combine(Path.GetTempPath(), $"rhino-gh-mcp-skill-{Guid.NewGuid():N}.gh");
                    File.WriteAllBytes(tmpPath, bytes);

                    var doc = GetGHDocument();
                    if (doc == null) { result = ErrorResponse("No active Grasshopper document."); return; }

                    var archive = new GH_IO.Serialization.GH_Archive();
                    if (!archive.ReadFromFile(tmpPath))
                    {
                        result = ErrorResponse("Failed to parse .gh archive.");
                        return;
                    }

                    // MergeWith places the archive's objects into the existing
                    // doc — preferred over Open which replaces the document.
                    var loaded = new GH_Document();
                    if (!archive.ExtractObject(loaded, "Definition"))
                    {
                        result = ErrorResponse("Archive did not contain a 'Definition' root.");
                        return;
                    }

                    int placedCount = loaded.ObjectCount;
                    var placedGuids = new List<Guid>();
                    // v0.2.4 bug fix: snapshot GUIDs + original pivots BEFORE
                    // MergeDocument moves the objects out of `loaded`. Without
                    // this, the post-merge foreach over loaded.Objects is empty
                    // and loaded_guids comes back as [] even on a 64-component
                    // merge.
                    var beforeMerge = new List<(Guid guid, System.Drawing.PointF originalPivot)>();
                    var minPt = new System.Drawing.PointF(float.MaxValue, float.MaxValue);
                    foreach (var obj in loaded.Objects)
                    {
                        var p = obj.Attributes?.Pivot ?? new System.Drawing.PointF(0, 0);
                        beforeMerge.Add((obj.InstanceGuid, p));
                        if (obj.Attributes != null)
                        {
                            if (p.X < minPt.X) minPt = new System.Drawing.PointF(p.X, minPt.Y);
                            if (p.Y < minPt.Y) minPt = new System.Drawing.PointF(minPt.X, p.Y);
                        }
                    }
                    float dx = pivotX - (minPt.X == float.MaxValue ? 0 : minPt.X);
                    float dy = pivotY - (minPt.Y == float.MaxValue ? 0 : minPt.Y);

                    doc.MergeDocument(loaded, true, true);

                    // Walk by GUID (objects now live in `doc`, not `loaded`).
                    foreach (var (guid, originalPivot) in beforeMerge)
                    {
                        var obj = doc.FindObject(guid, false);
                        if (obj?.Attributes != null)
                        {
                            obj.Attributes.Pivot = new System.Drawing.PointF(
                                originalPivot.X + dx, originalPivot.Y + dy);
                        }
                        placedGuids.Add(guid);
                        RecordTurnChange(guid);
                    }
                    doc.NewSolution(false);

                    var arr = new JArray();
                    foreach (var g in placedGuids) arr.Add(g.ToString());
                    result = SuccessResponse(new JObject
                    {
                        ["loaded_count"] = placedCount,
                        ["pivot_x"] = pivotX,
                        ["pivot_y"] = pivotY,
                        ["loaded_guids"] = arr,
                    });
                }
                catch (Exception ex) { error = ex; }
                finally
                {
                    if (tmpPath != null) { try { File.Delete(tmpPath); } catch { } }
                    done.Set();
                }
            });

            done.Wait(30000);
            if (error != null) return ErrorResponse($"Error loading definition: {error.Message}");
            return result ?? ErrorResponse("Operation timed out");
        }

        // ============================================================
        // v0.2.3 — productivity tools: bake, reference, panel, group,
        // move, organize, get-output. Each is a thin wrapper around the
        // existing GH / Rhino APIs; the value-add is exposing them
        // through the MCP surface so the AI can use them autonomously.
        // ============================================================

        /// <summary>
        /// Bake a component's outputs into the active Rhino document.
        /// Walks every output of the target component, asks each piece of
        /// volatile data to bake itself via IGH_BakeAwareData, and returns
        /// the list of Rhino GUIDs that landed in the doc.
        /// </summary>
        private JObject BakeToRhino(JObject cmd)
        {
            string guidStr = (string)cmd["instance_guid"];
            string layerName = (string)cmd["layer"];  // optional
            if (string.IsNullOrEmpty(guidStr))
                return ErrorResponse("`instance_guid` is required.");

            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);
            RunOnUiThread(() =>
            {
                try
                {
                    var doc = GetGHDocument();
                    if (doc == null) { result = ErrorResponse("No active GH document."); return; }
                    var obj = doc.FindObject(new Guid(guidStr), false);
                    if (obj == null) { result = ErrorResponse($"No object with guid {guidStr}"); return; }

                    var rhinoDoc = Rhino.RhinoDoc.ActiveDoc;
                    if (rhinoDoc == null) { result = ErrorResponse("No active Rhino document."); return; }

                    var attr = new Rhino.DocObjects.ObjectAttributes();
                    if (!string.IsNullOrEmpty(layerName))
                    {
                        int layerIdx = rhinoDoc.Layers.FindByFullPath(layerName, -1);
                        if (layerIdx < 0)
                            layerIdx = rhinoDoc.Layers.Add(layerName, System.Drawing.Color.Black);
                        if (layerIdx >= 0) attr.LayerIndex = layerIdx;
                    }

                    var bakedGuids = new List<Guid>();
                    int errorCount = 0;

                    // Path 1: component-level bake (preferred).
                    if (obj is IGH_BakeAwareObject bakeObj)
                    {
                        bakeObj.BakeGeometry(rhinoDoc, attr, bakedGuids);
                    }
                    // Path 2: walk outputs, bake each goo individually.
                    else if (obj is IGH_Component comp)
                    {
                        foreach (var output in comp.Params.Output)
                        {
                            foreach (var goo in output.VolatileData.AllData(true))
                            {
                                if (goo is IGH_BakeAwareData bakeGoo)
                                {
                                    try
                                    {
                                        if (bakeGoo.BakeGeometry(rhinoDoc, attr, out var bg))
                                            bakedGuids.Add(bg);
                                    }
                                    catch { errorCount++; }
                                }
                            }
                        }
                    }
                    else if (obj is IGH_Param param)
                    {
                        foreach (var goo in param.VolatileData.AllData(true))
                        {
                            if (goo is IGH_BakeAwareData bakeGoo)
                            {
                                try
                                {
                                    if (bakeGoo.BakeGeometry(rhinoDoc, attr, out var bg))
                                        bakedGuids.Add(bg);
                                }
                                catch { errorCount++; }
                            }
                        }
                    }

                    if (bakedGuids.Count > 0) rhinoDoc.Views.Redraw();

                    var arr = new JArray();
                    foreach (var g in bakedGuids) arr.Add(g.ToString());
                    result = SuccessResponse(new JObject
                    {
                        ["baked_count"] = bakedGuids.Count,
                        ["baked_rhino_guids"] = arr,
                        ["layer"] = layerName ?? rhinoDoc.Layers.CurrentLayer.FullPath,
                        ["error_count"] = errorCount,
                    });
                }
                catch (Exception ex) { error = ex; }
                finally { done.Set(); }
            });
            done.Wait(15000);
            if (error != null) return ErrorResponse($"Error in BakeToRhino: {error.Message}");
            return result ?? ErrorResponse("Bake timed out");
        }

        /// <summary>
        /// Drop a Curve / Brep / Mesh / Point / Surface / Geometry param on
        /// the GH canvas with persistent data referencing a Rhino object by
        /// GUID. This is the "right-click → Set one Curve" workflow exposed
        /// to the AI. Type is auto-detected from the Rhino object's geometry.
        /// </summary>
        private JObject ReferenceRhinoObject(JObject cmd)
        {
            string rhinoGuidStr = (string)cmd["rhino_guid"];
            float x = (float?)cmd["x"] ?? 100f;
            float y = (float?)cmd["y"] ?? 100f;
            if (string.IsNullOrEmpty(rhinoGuidStr))
                return ErrorResponse("`rhino_guid` is required.");

            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);
            RunOnUiThread(() =>
            {
                try
                {
                    var rhinoDoc = Rhino.RhinoDoc.ActiveDoc;
                    if (rhinoDoc == null) { result = ErrorResponse("No active Rhino document."); return; }

                    Guid rhinoGuid = new Guid(rhinoGuidStr);
                    var rhinoObj = rhinoDoc.Objects.Find(rhinoGuid);
                    if (rhinoObj == null) { result = ErrorResponse($"No Rhino object with guid {rhinoGuidStr}"); return; }

                    var doc = GetGHDocument();
                    if (doc == null) { result = ErrorResponse("No active GH document."); return; }

                    // Determine the right param type for this Rhino geometry.
                    string paramType;
                    IGH_Param param;
                    var geom = rhinoObj.Geometry;
                    if (geom is Rhino.Geometry.Curve)
                    {
                        var p = new Grasshopper.Kernel.Parameters.Param_Curve();
                        var goo = new Grasshopper.Kernel.Types.GH_Curve();
                        goo.ReferenceID = rhinoGuid;
                        goo.LoadGeometry(rhinoDoc);
                        p.PersistentData.Append(goo, new Grasshopper.Kernel.Data.GH_Path(0));
                        param = p; paramType = "Curve";
                    }
                    else if (geom is Rhino.Geometry.Brep)
                    {
                        var p = new Grasshopper.Kernel.Parameters.Param_Brep();
                        var goo = new Grasshopper.Kernel.Types.GH_Brep();
                        goo.ReferenceID = rhinoGuid;
                        goo.LoadGeometry(rhinoDoc);
                        p.PersistentData.Append(goo, new Grasshopper.Kernel.Data.GH_Path(0));
                        param = p; paramType = "Brep";
                    }
                    else if (geom is Rhino.Geometry.Mesh)
                    {
                        var p = new Grasshopper.Kernel.Parameters.Param_Mesh();
                        var goo = new Grasshopper.Kernel.Types.GH_Mesh();
                        goo.ReferenceID = rhinoGuid;
                        goo.LoadGeometry(rhinoDoc);
                        p.PersistentData.Append(goo, new Grasshopper.Kernel.Data.GH_Path(0));
                        param = p; paramType = "Mesh";
                    }
                    else if (geom is Rhino.Geometry.Surface)
                    {
                        var p = new Grasshopper.Kernel.Parameters.Param_Surface();
                        var goo = new Grasshopper.Kernel.Types.GH_Surface();
                        goo.ReferenceID = rhinoGuid;
                        goo.LoadGeometry(rhinoDoc);
                        p.PersistentData.Append(goo, new Grasshopper.Kernel.Data.GH_Path(0));
                        param = p; paramType = "Surface";
                    }
                    else if (geom is Rhino.Geometry.Point)
                    {
                        var p = new Grasshopper.Kernel.Parameters.Param_Point();
                        var pt = ((Rhino.Geometry.Point)geom).Location;
                        var goo = new Grasshopper.Kernel.Types.GH_Point(pt);
                        p.PersistentData.Append(goo, new Grasshopper.Kernel.Data.GH_Path(0));
                        param = p; paramType = "Point";
                    }
                    else
                    {
                        // Fallback: unrecognized geometry kind. Drop a generic
                        // Geometry param without persistent data — caller can
                        // right-click → "Set one Geometry" to attach the ref.
                        var p = new Grasshopper.Kernel.Parameters.Param_Geometry();
                        param = p; paramType = geom?.GetType().Name ?? "Geometry";
                    }

                    param.NickName = rhinoObj.Name?.Trim();
                    if (string.IsNullOrEmpty(param.NickName)) param.NickName = paramType;
                    param.CreateAttributes();
                    param.Attributes.Pivot = new System.Drawing.PointF(x, y);
                    doc.AddObject(param, false);
                    RecordTurnChange(param.InstanceGuid);

                    result = SuccessResponse(new JObject
                    {
                        ["instance_guid"] = param.InstanceGuid.ToString(),
                        ["param_type"] = paramType,
                        ["rhino_guid"] = rhinoGuidStr,
                        ["x"] = x,
                        ["y"] = y,
                    });
                }
                catch (Exception ex) { error = ex; }
                finally { done.Set(); }
            });
            done.Wait(10000);
            if (error != null) return ErrorResponse($"Error in ReferenceRhinoObject: {error.Message}");
            return result ?? ErrorResponse("Operation timed out");
        }

        /// <summary>
        /// Add a Grasshopper Panel at (x, y) with the given text content.
        /// Useful for the AI to leave inline notes / labels next to its work.
        /// </summary>
        private JObject AddPanel(JObject cmd)
        {
            string text = (string)cmd["text"] ?? "";
            float x = (float?)cmd["x"] ?? 100f;
            float y = (float?)cmd["y"] ?? 100f;

            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);
            RunOnUiThread(() =>
            {
                try
                {
                    var doc = GetGHDocument();
                    if (doc == null) { result = ErrorResponse("No active GH document."); return; }
                    var panel = new Grasshopper.Kernel.Special.GH_Panel();
                    panel.UserText = text;
                    panel.Properties.Multiline = text.Contains("\n");
                    panel.CreateAttributes();
                    panel.Attributes.Pivot = new System.Drawing.PointF(x, y);
                    doc.AddObject(panel, false);
                    RecordTurnChange(panel.InstanceGuid);
                    result = SuccessResponse(new JObject
                    {
                        ["instance_guid"] = panel.InstanceGuid.ToString(),
                        ["x"] = x,
                        ["y"] = y,
                        ["text_length"] = text.Length,
                    });
                }
                catch (Exception ex) { error = ex; }
                finally { done.Set(); }
            });
            done.Wait(5000);
            if (error != null) return ErrorResponse($"Error in AddPanel: {error.Message}");
            return result ?? ErrorResponse("Operation timed out");
        }

        /// <summary>
        /// Change the text on an existing Panel.
        /// </summary>
        private JObject SetPanelContent(JObject cmd)
        {
            string guidStr = (string)cmd["instance_guid"];
            string text = (string)cmd["text"] ?? "";
            if (string.IsNullOrEmpty(guidStr))
                return ErrorResponse("`instance_guid` is required.");

            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);
            RunOnUiThread(() =>
            {
                try
                {
                    var doc = GetGHDocument();
                    if (doc == null) { result = ErrorResponse("No active GH document."); return; }
                    var obj = doc.FindObject(new Guid(guidStr), false);
                    if (obj == null) { result = ErrorResponse($"No object with guid {guidStr}"); return; }
                    if (!(obj is Grasshopper.Kernel.Special.GH_Panel panel))
                        return; // Will fall through to error below.
                    panel.UserText = text;
                    panel.Properties.Multiline = text.Contains("\n");
                    panel.ExpireSolution(false);
                    RecordTurnChange(panel.InstanceGuid);
                    result = SuccessResponse(new JObject
                    {
                        ["instance_guid"] = guidStr,
                        ["text_length"] = text.Length,
                    });
                }
                catch (Exception ex) { error = ex; }
                finally { done.Set(); }
            });
            done.Wait(5000);
            if (error != null) return ErrorResponse($"Error in SetPanelContent: {error.Message}");
            return result ?? ErrorResponse("Object is not a Panel, or operation timed out");
        }

        /// <summary>
        /// Read the volatile data on a component's output. Returns a
        /// data-tree summary (path + flattened value strings per branch).
        /// Used by the AI to explain what a component is actually producing.
        /// </summary>
        private JObject GetComponentOutput(JObject cmd)
        {
            string guidStr = (string)cmd["instance_guid"];
            string outputName = (string)cmd["output_name"];  // optional; first output if missing
            int maxItems = (int?)cmd["max_items"] ?? 100;
            if (string.IsNullOrEmpty(guidStr))
                return ErrorResponse("`instance_guid` is required.");

            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);
            RunOnUiThread(() =>
            {
                try
                {
                    var doc = GetGHDocument();
                    if (doc == null) { result = ErrorResponse("No active GH document."); return; }
                    var obj = doc.FindObject(new Guid(guidStr), false);
                    if (obj == null) { result = ErrorResponse($"No object with guid {guidStr}"); return; }

                    IGH_Param outputParam = null;
                    if (obj is IGH_Component comp)
                    {
                        if (comp.Params.Output.Count == 0)
                        {
                            result = ErrorResponse("Component has no outputs.");
                            return;
                        }
                        if (!string.IsNullOrEmpty(outputName))
                        {
                            outputParam = comp.Params.Output.FirstOrDefault(
                                p => p.Name == outputName || p.NickName == outputName);
                            if (outputParam == null)
                            {
                                result = ErrorResponse(
                                    $"No output named {outputName}. Available: " +
                                    string.Join(", ", comp.Params.Output.Select(p => p.NickName)));
                                return;
                            }
                        }
                        else
                        {
                            outputParam = comp.Params.Output[0];
                        }
                    }
                    else if (obj is IGH_Param p)
                    {
                        outputParam = p;
                    }
                    else
                    {
                        result = ErrorResponse("Object has no readable output.");
                        return;
                    }

                    var data = outputParam.VolatileData;
                    var branches = new JArray();
                    int totalItems = 0;
                    int branchCount = data.PathCount;
                    for (int b = 0; b < branchCount && totalItems < maxItems; b++)
                    {
                        var path = data.get_Path(b);
                        var branch = data.get_Branch(b);
                        var items = new JArray();
                        foreach (var goo in branch)
                        {
                            if (totalItems >= maxItems) break;
                            items.Add(goo?.ToString() ?? "null");
                            totalItems++;
                        }
                        branches.Add(new JObject
                        {
                            ["path"] = path.ToString(),
                            ["count"] = branch.Count,
                            ["values"] = items,
                        });
                    }
                    result = SuccessResponse(new JObject
                    {
                        ["instance_guid"] = guidStr,
                        ["output_name"] = outputParam.NickName,
                        ["output_type"] = outputParam.TypeName,
                        ["branch_count"] = branchCount,
                        ["item_count"] = data.DataCount,
                        ["truncated"] = totalItems >= maxItems && data.DataCount > maxItems,
                        ["branches"] = branches,
                    });
                }
                catch (Exception ex) { error = ex; }
                finally { done.Set(); }
            });
            done.Wait(10000);
            if (error != null) return ErrorResponse($"Error in GetComponentOutput: {error.Message}");
            return result ?? ErrorResponse("Operation timed out");
        }

        /// <summary>
        /// Wrap a list of components in a Grasshopper Group (visual cluster
        /// with an optional nickname and color). Sees the standard Group
        /// rectangle on the canvas.
        /// </summary>
        private JObject GroupComponents(JObject cmd)
        {
            var guidsArr = cmd["instance_guids"] as JArray;
            if (guidsArr == null || guidsArr.Count == 0)
                return ErrorResponse("`instance_guids` (array) is required.");
            string nickname = (string)cmd["nickname"] ?? "Group";
            string colorHex = (string)cmd["color"];

            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);
            RunOnUiThread(() =>
            {
                try
                {
                    var doc = GetGHDocument();
                    if (doc == null) { result = ErrorResponse("No active GH document."); return; }
                    var group = new Grasshopper.Kernel.Special.GH_Group();
                    group.NickName = nickname;
                    int added = 0;
                    foreach (var token in guidsArr)
                    {
                        var s = token?.ToString();
                        if (string.IsNullOrEmpty(s)) continue;
                        try
                        {
                            var g = new Guid(s);
                            group.AddObject(g);
                            added++;
                        }
                        catch { /* skip invalid */ }
                    }
                    if (!string.IsNullOrEmpty(colorHex))
                    {
                        try
                        {
                            var c = System.Drawing.ColorTranslator.FromHtml(colorHex);
                            group.Colour = c;
                        }
                        catch { /* keep default */ }
                    }
                    group.CreateAttributes();
                    doc.AddObject(group, false);
                    group.ExpireCaches();
                    RecordTurnChange(group.InstanceGuid);
                    result = SuccessResponse(new JObject
                    {
                        ["instance_guid"] = group.InstanceGuid.ToString(),
                        ["nickname"] = nickname,
                        ["members"] = added,
                    });
                }
                catch (Exception ex) { error = ex; }
                finally { done.Set(); }
            });
            done.Wait(5000);
            if (error != null) return ErrorResponse($"Error in GroupComponents: {error.Message}");
            return result ?? ErrorResponse("Operation timed out");
        }

        /// <summary>
        /// Move a canvas object to a new pivot. Lightweight — single
        /// component, single point. For layout-wide tidying use organize.
        /// </summary>
        private JObject MoveComponent(JObject cmd)
        {
            string guidStr = (string)cmd["instance_guid"];
            float x = (float?)cmd["x"] ?? 0f;
            float y = (float?)cmd["y"] ?? 0f;
            if (string.IsNullOrEmpty(guidStr))
                return ErrorResponse("`instance_guid` is required.");

            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);
            RunOnUiThread(() =>
            {
                try
                {
                    var doc = GetGHDocument();
                    if (doc == null) { result = ErrorResponse("No active GH document."); return; }
                    var obj = doc.FindObject(new Guid(guidStr), false);
                    if (obj?.Attributes == null) { result = ErrorResponse($"No object with guid {guidStr}"); return; }
                    obj.Attributes.Pivot = new System.Drawing.PointF(x, y);
                    obj.Attributes.ExpireLayout();
                    Grasshopper.Instances.ActiveCanvas?.Refresh();
                    RecordTurnChange(obj.InstanceGuid);
                    result = SuccessResponse(new JObject
                    {
                        ["instance_guid"] = guidStr,
                        ["x"] = x,
                        ["y"] = y,
                    });
                }
                catch (Exception ex) { error = ex; }
                finally { done.Set(); }
            });
            done.Wait(5000);
            if (error != null) return ErrorResponse($"Error in MoveComponent: {error.Message}");
            return result ?? ErrorResponse("Operation timed out");
        }

        /// <summary>
        /// Auto-layout components left → right by data-flow depth. Depth is
        /// the longest path from a "source" (no in-set predecessors) to the
        /// component. Components at the same depth stack vertically.
        ///
        /// Accepts an explicit `instance_guids` list to layout, or omits it
        /// to layout every component on the canvas. Components NOT in the
        /// set are ignored when computing depth (their positions stay put).
        /// </summary>
        private JObject OrganizeComponents(JObject cmd)
        {
            var guidsArr = cmd["instance_guids"] as JArray;
            float startX = (float?)cmd["start_x"] ?? 100f;
            float startY = (float?)cmd["start_y"] ?? 100f;
            float colWidth = (float?)cmd["column_width"] ?? 250f;
            float rowHeight = (float?)cmd["row_height"] ?? 110f;

            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);
            RunOnUiThread(() =>
            {
                try
                {
                    var doc = GetGHDocument();
                    if (doc == null) { result = ErrorResponse("No active GH document."); return; }

                    // Collect the target set.
                    var targets = new Dictionary<Guid, IGH_DocumentObject>();
                    if (guidsArr != null && guidsArr.Count > 0)
                    {
                        foreach (var token in guidsArr)
                        {
                            var s = token?.ToString();
                            if (string.IsNullOrEmpty(s)) continue;
                            try
                            {
                                var g = new Guid(s);
                                var o = doc.FindObject(g, false);
                                if (o != null) targets[g] = o;
                            }
                            catch { }
                        }
                    }
                    else
                    {
                        foreach (var o in doc.Objects) targets[o.InstanceGuid] = o;
                    }
                    if (targets.Count == 0)
                    {
                        result = ErrorResponse("No target objects to organize.");
                        return;
                    }

                    // Compute depth via memoized DFS.
                    var depth = new Dictionary<Guid, int>();
                    int ComputeDepth(IGH_DocumentObject o, HashSet<Guid> visiting)
                    {
                        if (depth.TryGetValue(o.InstanceGuid, out var d)) return d;
                        if (!visiting.Add(o.InstanceGuid))
                        {
                            // Cycle — bail out at current depth 0.
                            depth[o.InstanceGuid] = 0;
                            return 0;
                        }
                        int maxUpstream = -1;
                        if (o is IGH_Component cc)
                        {
                            foreach (var input in cc.Params.Input)
                            {
                                foreach (var src in input.Sources)
                                {
                                    IGH_DocumentObject srcObj = src;
                                    // If the source is a sub-param of a component, walk up.
                                    if (src.Attributes?.GetTopLevel?.DocObject is IGH_DocumentObject top)
                                        srcObj = top;
                                    if (srcObj == null) continue;
                                    if (!targets.ContainsKey(srcObj.InstanceGuid)) continue;
                                    maxUpstream = Math.Max(maxUpstream, ComputeDepth(srcObj, visiting));
                                }
                            }
                        }
                        else if (o is IGH_Param pp)
                        {
                            foreach (var src in pp.Sources)
                            {
                                IGH_DocumentObject srcObj = src;
                                if (src.Attributes?.GetTopLevel?.DocObject is IGH_DocumentObject top)
                                    srcObj = top;
                                if (srcObj == null) continue;
                                if (!targets.ContainsKey(srcObj.InstanceGuid)) continue;
                                maxUpstream = Math.Max(maxUpstream, ComputeDepth(srcObj, visiting));
                            }
                        }
                        visiting.Remove(o.InstanceGuid);
                        var result = maxUpstream + 1;
                        depth[o.InstanceGuid] = result;
                        return result;
                    }

                    foreach (var o in targets.Values)
                        ComputeDepth(o, new HashSet<Guid>());

                    // Group by depth, sort each column by current y for stability.
                    var byDepth = new Dictionary<int, List<IGH_DocumentObject>>();
                    foreach (var kv in depth)
                    {
                        if (!targets.TryGetValue(kv.Key, out var o)) continue;
                        if (!byDepth.TryGetValue(kv.Value, out var list))
                        {
                            list = new List<IGH_DocumentObject>();
                            byDepth[kv.Value] = list;
                        }
                        list.Add(o);
                    }

                    int placed = 0;
                    foreach (var col in byDepth.OrderBy(p => p.Key))
                    {
                        var sorted = col.Value
                            .OrderBy(o => o.Attributes?.Pivot.Y ?? 0)
                            .ToList();
                        for (int i = 0; i < sorted.Count; i++)
                        {
                            var o = sorted[i];
                            if (o.Attributes == null) continue;
                            o.Attributes.Pivot = new System.Drawing.PointF(
                                startX + col.Key * colWidth,
                                startY + i * rowHeight);
                            o.Attributes.ExpireLayout();
                            placed++;
                        }
                    }

                    Grasshopper.Instances.ActiveCanvas?.Refresh();

                    result = SuccessResponse(new JObject
                    {
                        ["placed"] = placed,
                        ["columns"] = byDepth.Count,
                        ["start_x"] = startX,
                        ["start_y"] = startY,
                    });
                }
                catch (Exception ex) { error = ex; }
                finally { done.Set(); }
            });
            done.Wait(15000);
            if (error != null) return ErrorResponse($"Error in OrganizeComponents: {error.Message}");
            return result ?? ErrorResponse("Operation timed out");
        }

        // ============================================================
        // v0.2.4 — fast outline tools. Replace the multi-call discovery
        // chain (gh_canvas_summary -> gh_get_context simplified -> 111k
        // chars -> subagent fork) with one cheap server-side call that
        // returns clusters + endpoints + inputs in ~1k chars.
        //
        // Cluster definition: connected components on the WIRE graph.
        // Two canvas objects are in the same cluster iff any wire path
        // (treating wires as undirected) connects them.
        //
        // Output format is intentionally short — JSON with short integer
        // IDs ("c1", "c2") instead of 36-char GUIDs in the structural
        // section, plus a single `guids` lookup table at the end. Type
        // names are stripped of "GH_" / "Component_" / "Param_" prefixes
        // and the trailing "Component" word.
        // ============================================================

        // Cache: cluster_id -> list of GUIDs. Updated by canvas_outline /
        // file_outline, read by cluster_flow. Lives at the process level
        // — sufficient since the AI's discovery flow is "outline then
        // immediately drill down" within one conversation.
        private static readonly Dictionary<int, List<Guid>> _outlineClusterCache =
            new Dictionary<int, List<Guid>>();
        private static GH_Document _outlineCacheDoc = null;
        private static readonly object _outlineCacheLock = new object();

        private static string ShortKind(IGH_DocumentObject obj)
        {
            if (obj == null) return "?";
            string t = obj.GetType().Name;
            if (t.StartsWith("GH_")) t = t.Substring(3);
            else if (t.StartsWith("Component_")) t = t.Substring(10);
            else if (t.StartsWith("Param_")) t = t.Substring(6);
            if (t.EndsWith("Component") && t.Length > 9) t = t.Substring(0, t.Length - 9);
            return t;
        }

        // Union-find for clustering by wire graph.
        private class DSU
        {
            private readonly Dictionary<Guid, Guid> _parent = new Dictionary<Guid, Guid>();
            public void Add(Guid g) { if (!_parent.ContainsKey(g)) _parent[g] = g; }
            public Guid Find(Guid g)
            {
                if (!_parent.ContainsKey(g)) _parent[g] = g;
                while (_parent[g] != g)
                {
                    _parent[g] = _parent[_parent[g]];
                    g = _parent[g];
                }
                return g;
            }
            public void Union(Guid a, Guid b)
            {
                var ra = Find(a); var rb = Find(b);
                if (ra != rb) _parent[ra] = rb;
            }
        }

        // Walk wires, build DSU, return clusters as List<List<top-level object>>.
        private static List<List<IGH_DocumentObject>> ComputeClusters(GH_Document doc)
        {
            var dsu = new DSU();
            var byGuid = new Dictionary<Guid, IGH_DocumentObject>();
            foreach (var obj in doc.Objects)
            {
                dsu.Add(obj.InstanceGuid);
                byGuid[obj.InstanceGuid] = obj;
            }

            Guid TopGuidOf(IGH_Param p)
            {
                IGH_DocumentObject top = p;
                var parent = p.Attributes?.Parent?.DocObject;
                if (parent is IGH_DocumentObject d) top = d;
                return top.InstanceGuid;
            }

            void UnionWires(IGH_Param param, Guid topGuid)
            {
                foreach (var src in param.Sources)
                {
                    var srcTopGuid = TopGuidOf(src);
                    if (byGuid.ContainsKey(srcTopGuid))
                        dsu.Union(topGuid, srcTopGuid);
                }
                foreach (var rec in param.Recipients)
                {
                    var dstTopGuid = TopGuidOf(rec);
                    if (byGuid.ContainsKey(dstTopGuid))
                        dsu.Union(topGuid, dstTopGuid);
                }
            }

            foreach (var obj in doc.Objects)
            {
                if (obj is IGH_Component c)
                {
                    foreach (var p in c.Params.Input) UnionWires(p, c.InstanceGuid);
                    foreach (var p in c.Params.Output) UnionWires(p, c.InstanceGuid);
                }
                else if (obj is IGH_Param p)
                {
                    UnionWires(p, p.InstanceGuid);
                }
            }

            var groups = new Dictionary<Guid, List<IGH_DocumentObject>>();
            foreach (var obj in doc.Objects)
            {
                var root = dsu.Find(obj.InstanceGuid);
                if (!groups.TryGetValue(root, out var list))
                {
                    list = new List<IGH_DocumentObject>();
                    groups[root] = list;
                }
                list.Add(obj);
            }
            return groups.Values
                .OrderByDescending(g => g.Count)
                .ToList();
        }

        // Build the compact outline JSON for a document.
        private static JObject BuildOutline(GH_Document doc, List<List<IGH_DocumentObject>> clusters)
        {
            // Allocate short IDs only for endpoints + inputs (the only objects we
            // surface by reference). Order: c1, c2, ... assigned on first emit.
            var shortIds = new Dictionary<Guid, string>();
            var guidsTable = new JObject();
            string EnsureShortId(IGH_DocumentObject obj)
            {
                if (shortIds.TryGetValue(obj.InstanceGuid, out var sid)) return sid;
                sid = "c" + (shortIds.Count + 1);
                shortIds[obj.InstanceGuid] = sid;
                guidsTable[sid] = obj.InstanceGuid.ToString();
                return sid;
            }

            var clusterArr = new JArray();
            int clusterId = 0;
            foreach (var cluster in clusters)
            {
                clusterId++;
                // Kinds histogram, top entries by count.
                var kindCounts = new Dictionary<string, int>();
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                foreach (var obj in cluster)
                {
                    var k = ShortKind(obj);
                    kindCounts[k] = kindCounts.TryGetValue(k, out var n) ? n + 1 : 1;
                    var p = obj.Attributes?.Pivot;
                    if (p.HasValue)
                    {
                        if (p.Value.X < minX) minX = p.Value.X;
                        if (p.Value.Y < minY) minY = p.Value.Y;
                        if (p.Value.X > maxX) maxX = p.Value.X;
                        if (p.Value.Y > maxY) maxY = p.Value.Y;
                    }
                }
                // Endpoints: components/params with no downstream consumer
                // INSIDE this cluster. Limit to first 5.
                var clusterGuids = new HashSet<Guid>(cluster.Select(o => o.InstanceGuid));
                var endpoints = new JArray();
                int epCount = 0;
                foreach (var obj in cluster)
                {
                    if (epCount >= 5) break;
                    bool hasDownstream = false;
                    var outputs = new List<IGH_Param>();
                    if (obj is IGH_Component cc) outputs.AddRange(cc.Params.Output);
                    else if (obj is IGH_Param pp) outputs.Add(pp);
                    foreach (var op in outputs)
                    {
                        foreach (var rec in op.Recipients)
                        {
                            var top = rec.Attributes?.Parent?.DocObject is IGH_DocumentObject d
                                ? d.InstanceGuid : rec.InstanceGuid;
                            if (clusterGuids.Contains(top) && top != obj.InstanceGuid)
                            { hasDownstream = true; break; }
                        }
                        if (hasDownstream) break;
                    }
                    if (!hasDownstream && outputs.Count > 0)
                    {
                        endpoints.Add(new JObject
                        {
                            ["c"] = EnsureShortId(obj),
                            ["k"] = ShortKind(obj),
                        });
                        epCount++;
                    }
                }
                // Inputs: widgets (sliders / toggles / value-lists / panels). Limit 10.
                var inputs = new JArray();
                int inCount = 0;
                foreach (var obj in cluster)
                {
                    if (inCount >= 10) break;
                    if (obj is GH_NumberSlider || obj is GH_BooleanToggle
                        || obj is GH_ValueList || obj is GH_Panel)
                    {
                        inputs.Add(new JObject
                        {
                            ["c"] = EnsureShortId(obj),
                            ["k"] = ShortKind(obj),
                            ["n"] = obj.NickName ?? "",
                        });
                        inCount++;
                    }
                }
                var kindsArr = new JArray();
                foreach (var kv in kindCounts.OrderByDescending(p => p.Value).Take(8))
                {
                    kindsArr.Add(new JArray { kv.Key, kv.Value });
                }
                clusterArr.Add(new JObject
                {
                    ["i"] = clusterId,
                    ["size"] = cluster.Count,
                    ["bbox"] = new JArray { minX, minY, maxX, maxY },
                    ["kinds"] = kindsArr,
                    ["ends"] = endpoints,
                    ["ins"] = inputs,
                });
            }
            return new JObject
            {
                ["v"] = 1,
                ["n"] = doc.ObjectCount,
                ["cn"] = clusters.Count,
                ["clusters"] = clusterArr,
                ["guids"] = guidsTable,
            };
        }

        private static void UpdateClusterCache(GH_Document doc, List<List<IGH_DocumentObject>> clusters)
        {
            lock (_outlineCacheLock)
            {
                _outlineClusterCache.Clear();
                _outlineCacheDoc = doc;
                int id = 0;
                foreach (var cluster in clusters)
                {
                    id++;
                    _outlineClusterCache[id] = cluster.Select(o => o.InstanceGuid).ToList();
                }
            }
        }

        private JObject CanvasOutline(JObject cmd)
        {
            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);
            RunOnUiThread(() =>
            {
                try
                {
                    var doc = GetGHDocument();
                    if (doc == null) { result = ErrorResponse("No active GH document."); return; }
                    var clusters = ComputeClusters(doc);
                    UpdateClusterCache(doc, clusters);
                    result = SuccessResponse(BuildOutline(doc, clusters));
                }
                catch (Exception ex) { error = ex; }
                finally { done.Set(); }
            });
            done.Wait(10000);
            if (error != null) return ErrorResponse($"Error in CanvasOutline: {error.Message}");
            return result ?? ErrorResponse("Outline timed out");
        }

        // file_outline — same shape as canvas_outline but operates on a
        // .gh / .ghx file read off disk. The bridge loads it into a TEMP
        // GH_Document (never added to the canvas, never solved), parses
        // clusters, then disposes the temp doc.
        private JObject FileOutlineFromBase64(JObject cmd)
        {
            string b64 = (string)cmd["data"];
            if (string.IsNullOrEmpty(b64))
                return ErrorResponse("`data` is required (base64-encoded .gh / .ghx bytes).");

            byte[] bytes;
            try { bytes = Convert.FromBase64String(b64); }
            catch (Exception ex) { return ErrorResponse($"Bad base64 payload: {ex.Message}"); }

            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);
            RunOnUiThread(() =>
            {
                string tmpPath = null;
                try
                {
                    tmpPath = Path.Combine(Path.GetTempPath(), $"rhino-gh-mcp-outline-{Guid.NewGuid():N}.gh");
                    File.WriteAllBytes(tmpPath, bytes);

                    var archive = new GH_IO.Serialization.GH_Archive();
                    if (!archive.ReadFromFile(tmpPath))
                    {
                        result = ErrorResponse("Failed to parse .gh / .ghx archive.");
                        return;
                    }
                    var tempDoc = new GH_Document();
                    if (!archive.ExtractObject(tempDoc, "Definition"))
                    {
                        result = ErrorResponse("Archive did not contain a 'Definition' root.");
                        return;
                    }
                    var clusters = ComputeClusters(tempDoc);
                    UpdateClusterCache(tempDoc, clusters);
                    result = SuccessResponse(BuildOutline(tempDoc, clusters));
                }
                catch (Exception ex) { error = ex; }
                finally
                {
                    if (tmpPath != null) { try { File.Delete(tmpPath); } catch { } }
                    done.Set();
                }
            });
            done.Wait(20000);
            if (error != null) return ErrorResponse($"Error in FileOutline: {error.Message}");
            return result ?? ErrorResponse("Outline timed out");
        }

        // cluster_flow — given a cluster_id returned by the most recent
        // outline call, return the stage-based dataflow inside that
        // cluster. Stages = topological depth within the cluster.
        private JObject ClusterFlow(JObject cmd)
        {
            int clusterId = (int?)cmd["cluster_id"] ?? 0;
            if (clusterId <= 0)
                return ErrorResponse("`cluster_id` (positive integer from a prior outline) is required.");

            List<Guid> memberGuids;
            GH_Document doc;
            lock (_outlineCacheLock)
            {
                if (!_outlineClusterCache.TryGetValue(clusterId, out memberGuids))
                    return ErrorResponse($"Unknown cluster_id {clusterId}. Call canvas_outline or file_outline first.");
                doc = _outlineCacheDoc;
            }
            if (doc == null) return ErrorResponse("Cluster cache has no document; outline first.");

            JObject result = null;
            Exception error = null;
            var done = new System.Threading.ManualResetEventSlim(false);
            RunOnUiThread(() =>
            {
                try
                {
                    var memberSet = new HashSet<Guid>(memberGuids);
                    var members = new List<IGH_DocumentObject>();
                    foreach (var g in memberGuids)
                    {
                        var o = doc.FindObject(g, false);
                        if (o != null) members.Add(o);
                    }
                    // Compute stage = longest upstream path inside cluster.
                    var depth = new Dictionary<Guid, int>();
                    int ComputeDepth(IGH_DocumentObject o, HashSet<Guid> visiting)
                    {
                        if (depth.TryGetValue(o.InstanceGuid, out var d)) return d;
                        if (!visiting.Add(o.InstanceGuid)) { depth[o.InstanceGuid] = 0; return 0; }
                        int maxUp = -1;
                        IEnumerable<IGH_Param> ins = new IGH_Param[0];
                        if (o is IGH_Component cc) ins = cc.Params.Input;
                        else if (o is IGH_Param pp) ins = new[] { pp };
                        foreach (var p in ins)
                        {
                            foreach (var src in p.Sources)
                            {
                                var top = src.Attributes?.Parent?.DocObject is IGH_DocumentObject t
                                    ? t.InstanceGuid : src.InstanceGuid;
                                if (!memberSet.Contains(top)) continue;
                                var so = doc.FindObject(top, false);
                                if (so != null)
                                    maxUp = Math.Max(maxUp, ComputeDepth(so, visiting));
                            }
                        }
                        visiting.Remove(o.InstanceGuid);
                        var v = maxUp + 1;
                        depth[o.InstanceGuid] = v;
                        return v;
                    }
                    foreach (var m in members) ComputeDepth(m, new HashSet<Guid>());

                    // Group by depth; within each stage, count by kind.
                    var byStage = members
                        .GroupBy(o => depth[o.InstanceGuid])
                        .OrderBy(g => g.Key);
                    var stagesArr = new JArray();
                    foreach (var stage in byStage)
                    {
                        var kindCount = new Dictionary<string, int>();
                        var nicknames = new List<string>();
                        foreach (var o in stage)
                        {
                            var k = ShortKind(o);
                            kindCount[k] = kindCount.TryGetValue(k, out var n) ? n + 1 : 1;
                            if (o is GH_NumberSlider || o is GH_BooleanToggle
                                || o is GH_ValueList)
                            {
                                if (!string.IsNullOrEmpty(o.NickName) && nicknames.Count < 8)
                                    nicknames.Add(o.NickName);
                            }
                        }
                        var kindArr = new JArray();
                        foreach (var kv in kindCount.OrderByDescending(p => p.Value))
                            kindArr.Add(new JArray { kv.Key, kv.Value });
                        var stageObj = new JObject
                        {
                            ["s"] = stage.Key,
                            ["kinds"] = kindArr,
                        };
                        if (nicknames.Count > 0)
                        {
                            var ns = new JArray();
                            foreach (var n in nicknames) ns.Add(n);
                            stageObj["nicks"] = ns;
                        }
                        stagesArr.Add(stageObj);
                    }
                    result = SuccessResponse(new JObject
                    {
                        ["cluster_id"] = clusterId,
                        ["member_count"] = members.Count,
                        ["stage_count"] = stagesArr.Count,
                        ["stages"] = stagesArr,
                    });
                }
                catch (Exception ex) { error = ex; }
                finally { done.Set(); }
            });
            done.Wait(10000);
            if (error != null) return ErrorResponse($"Error in ClusterFlow: {error.Message}");
            return result ?? ErrorResponse("Operation timed out");
        }
    }
}
