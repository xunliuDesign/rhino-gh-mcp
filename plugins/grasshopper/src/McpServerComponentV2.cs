using System;
using System.Threading;
using Grasshopper.Kernel;

namespace RhinoGhMcp
{
    /// <summary>
    /// v0.2 canvas component. Reframes the v0.1 capability flags as a single
    /// **Scenario** dropdown. Five scenarios map onto the underlying flags:
    ///
    ///     0 = Inspect : params=R, components=R, scripting=R          (no writes)
    ///     1 = Tune    : params=W, components=R, scripting=R
    ///     2 = Coach   : params=W, components=W, scripting=R, curated scope (default)
    ///     3 = Execute : (Skill-defined; mirrors Coach until the Skill overrides)
    ///     4 = Author  : params=W, components=W, scripting=W, defaults scope
    ///
    /// Per the v0.2 redesign doc (docs/v0.2-redesign.md) the raw flags are not
    /// removed — they are exposed as **Advanced** inputs (one Override*
    /// parameter each, default -1 = "use Scenario default"). 0 / 1 explicitly
    /// force off / on. This satisfies research workflows that need a non-
    /// standard combination.
    ///
    /// The v0.1 component is kept on disk and registered with the host so
    /// existing .gh files continue to load — v0.2 ships a new GUID so the two
    /// coexist in the ribbon. The class inherits McpServerComponent to reuse
    /// its TCP listener + dispatch infrastructure verbatim; the only behavior
    /// we override is input registration and SolveInstance (the Scenario →
    /// capability mapping).
    /// </summary>
    public class McpServerComponentV2 : McpServerComponent
    {
        public McpServerComponentV2()
            : base()
        {
            // Override name/nickname/description via reflection on the proxy —
            // ctor base() already invoked the v1 names. GH_Component pulls
            // these from internal fields populated by the base ctor, but it
            // also calls Name/NickName/Description virtuals at display time,
            // and overriding those is the cleaner path.
        }

        // Override the descriptive metadata. GH_Component asks these on every
        // ribbon paint, so overriding here propagates to the UI cleanly.
        public override string Name => "rhino-gh-mcp Server (v2)";
        public override string NickName => "MCPv2";
        public override string Description =>
            "Hosts the rhino-gh-mcp v2 command bridge for the Python MCP server. " +
            "Five-scenario surface (Inspect/Tune/Coach/Execute/Author). " +
            "Replaces v0.1's 8-input capability surface — the raw flags are still " +
            "available under the Advanced overrides. Drop one on the canvas, " +
            "pick a Scenario, set Run=True.";

        public override string Category => "MCP";
        public override string SubCategory => "Server";

        // Fresh v2 GUID, distinct from v0.1's 005a98bf-a9f8-4e11-a96a-ea4eafb59c4c
        // so old definitions on the v0.1 component keep loading correctly.
        public override Guid ComponentGuid =>
            new Guid("0b2c0001-d2e2-4a02-9a7b-7c2f0b2c0001");

        public override GH_Exposure Exposure => GH_Exposure.primary;

        /// <summary>
        /// v0.2 input layout. The default-visible inputs are minimal; advanced
        /// users can right-click to expose the override switches.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            // --- Default-visible ribbon view ---------------------------------
            pManager.AddBooleanParameter("RunServer", "Run",
                "Start/stop the MCP server.",
                GH_ParamAccess.item, false);

            // Scenario: integer enum 0..4. The Grasshopper value-list pattern
            // would be nicer UX but adds a separate value-list parameter; an
            // integer input is honest about being a forwarded knob and keeps
            // the surface in line with v0.1's other inputs.
            pManager.AddIntegerParameter("Scenario", "Scenario",
                "What you're trying to do — drives capability flags internally.\n" +
                "0 = Inspect (no writes, read-only)\n" +
                "1 = Tune (sliders/toggles only)\n" +
                "2 = Coach (Skill-guided edits + change tracking)  [DEFAULT]\n" +
                "3 = Execute (Skill commands only, no improvisation)\n" +
                "4 = Author (full freedom, including scripting)",
                GH_ParamAccess.item, 2);

            pManager.AddTextParameter("ActiveSkill", "Skill",
                "Skill id (e.g. 'landform', 'ladybug-environmental') the AI is " +
                "currently following. Leave empty to use full ribbon. The Python " +
                "server reads this on every turn and gates accordingly.",
                GH_ParamAccess.item, "");

            pManager.AddIntegerParameter("Port", "Port",
                "TCP port the MCP HTTP listener binds to. Must match the Python " +
                "server's --gh-port.",
                GH_ParamAccess.item, 9999);

            // --- Advanced overrides ------------------------------------------
            // Default = -1 meaning "use Scenario default". 0 or 1 explicitly
            // force off / on. ComponentScope override accepts -1 (Scenario
            // default), 0 (curated), 1 (defaults), 2 (all).
            //
            // These are typed as integers (not booleans) so we can encode the
            // three-state "auto / off / on" cleanly. Marked Optional so users
            // can leave them disconnected without an error.
            int idx = pManager.AddIntegerParameter("OverrideAllowParameters", "ovParams",
                "Advanced: -1 (auto from Scenario), 0 (off), 1 (on).",
                GH_ParamAccess.item, -1);
            pManager[idx].Optional = true;
            idx = pManager.AddIntegerParameter("OverrideAllowComponents", "ovComp",
                "Advanced: -1 (auto from Scenario), 0 (off), 1 (on).",
                GH_ParamAccess.item, -1);
            pManager[idx].Optional = true;
            idx = pManager.AddIntegerParameter("OverrideAllowScripting", "ovScript",
                "Advanced: -1 (auto from Scenario), 0 (off), 1 (on). Powerful — " +
                "the AI can write arbitrary code into Script components.",
                GH_ParamAccess.item, -1);
            pManager[idx].Optional = true;
            idx = pManager.AddIntegerParameter("OverrideComponentScope", "ovScope",
                "Advanced: -1 (auto), 0 (curated), 1 (defaults), 2 (all).",
                GH_ParamAccess.item, -1);
            pManager[idx].Optional = true;
            idx = pManager.AddBooleanParameter("AutoRecompute", "AutoRecomp",
                "Automatically recompute all after each command execution.",
                GH_ParamAccess.item, false);
            pManager[idx].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "Status",
                "Server status. Shows scenario + active skill when running.",
                GH_ParamAccess.item);
            pManager.AddTextParameter("DebugOutput", "Debug", "Debug/sticky info",
                GH_ParamAccess.item);
            pManager.AddTextParameter("Version", "Version",
                "Plugin version + commit identifier",
                GH_ParamAccess.item);
        }

        /// <summary>
        /// Maps Scenario → capability flags, applies any Advanced overrides,
        /// then delegates the actual server start/stop bookkeeping to the
        /// inherited fields.
        /// </summary>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Inputs (V2 ordering): Run, Scenario, ActiveSkill, Port,
            // Overrides (params, components, scripting, scope), AutoRecompute.
            bool run = false;
            int scenarioInt = 2; // Coach default
            string activeSkill = "";
            int portInput = 9999;
            int ovParams = -1;
            int ovComp = -1;
            int ovScript = -1;
            int ovScope = -1;
            bool autoRecompute = false;

            if (!DA.GetData(0, ref run)) return;
            DA.GetData(1, ref scenarioInt);
            DA.GetData(2, ref activeSkill);
            DA.GetData(3, ref portInput);
            DA.GetData(4, ref ovParams);
            DA.GetData(5, ref ovComp);
            DA.GetData(6, ref ovScript);
            DA.GetData(7, ref ovScope);
            DA.GetData(8, ref autoRecompute);

            // Clamp & resolve scenario.
            scenarioInt = Math.Max(0, Math.Min(4, scenarioInt));
            string scenarioName = ScenarioName(scenarioInt);

            // Default capability mapping for the scenario.
            bool baseAllowParams;
            bool baseAllowComp;
            bool baseAllowScript;
            int baseScope; // 0 curated, 1 defaults, 2 all
            DefaultsForScenario(scenarioInt, out baseAllowParams, out baseAllowComp,
                                out baseAllowScript, out baseScope);

            // Apply overrides (-1 = auto, 0 = off, 1 = on).
            currentAllowParameters = ResolveBoolOverride(ovParams, baseAllowParams);
            currentAllowComponents = ResolveBoolOverride(ovComp, baseAllowComp);
            currentAllowScripting = ResolveBoolOverride(ovScript, baseAllowScript);
            currentComponentScope = ovScope >= 0 && ovScope <= 2 ? ovScope : baseScope;
            currentAutoRecompute = autoRecompute;
            // V2 always treats the canvas categories filter as Skill-derived;
            // the v0.1 component used a string `CategoryFilter` input but in
            // v0.2 the Skill's frontmatter is the source of truth. For
            // backwards-compat (bridge command `set_component_parameter` etc.
            // still reads it), default to "MCP" so existing flows keep working.
            currentCategoryFilter = "MCP";

            // Surface scenario + skill via the bridge's `get_capabilities`
            // reply. The static fields live on the base class.
            currentScenario = scenarioName;
            currentActiveSkill = activeSkill ?? "";

            // Apply port from input. Only takes effect when the server isn't already running;
            // changing the port while running won't rebind — toggle Run off and on.
            if (serverThread == null || !serverThread.IsAlive)
                port = portInput;

            // Start/stop server logic (mirrors v0.1).
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
                lock (serverLock)
                {
                    if (listener != null)
                    {
                        try { listener.Stop(); } catch { }
                        listener = null;
                    }
                }
                if (!serverThread.Join(3000))
                {
                    LogError("Server thread did not stop gracefully within timeout");
                }
                serverThread = null;
            }

            // Compose a status line that includes scenario + active skill.
            string displayStatus = serverStatus;
            if (run && serverThread != null && serverThread.IsAlive)
            {
                string skillDisp = string.IsNullOrEmpty(currentActiveSkill)
                    ? "(none)" : currentActiveSkill;
                displayStatus = $"Server On {host}:{port} | Scenario: {Capitalize(scenarioName)} | Skill: {skillDisp}";
            }

            DA.SetData(0, displayStatus);
            DA.SetData(1, debugOutput);
            DA.SetData(2, V2VersionString);
        }

        private static readonly string V2VersionString = BuildV2VersionString();
        private static string BuildV2VersionString()
        {
            try
            {
                var asm = typeof(McpServerComponentV2).Assembly;
                var name = asm.GetName();
                return string.Format("rhino-gh-mcp v{0} (v2 scenarios) ({1})",
                    name.Version != null ? name.Version.ToString() : "0.0.0",
                    "https://github.com/xunliuDesign/rhino-gh-mcp");
            }
            catch
            {
                return "rhino-gh-mcp v? (v2) (https://github.com/xunliuDesign/rhino-gh-mcp)";
            }
        }

        /// <summary>
        /// Use the same icon as v0.1 (one server family). Could be swapped
        /// for a v2-specific badge later if visual disambiguation is wanted.
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

        // --- Scenario helpers ----------------------------------------------

        private static string ScenarioName(int idx)
        {
            switch (idx)
            {
                case 0: return "inspect";
                case 1: return "tune";
                case 2: return "coach";
                case 3: return "execute";
                case 4: return "author";
                default: return "coach";
            }
        }

        private static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpper(s[0]) + s.Substring(1);
        }

        /// <summary>
        /// The canonical Scenario → capability mapping. Mirrors the table in
        /// docs/v0.2-redesign.md. Execute mode is treated as a Coach-like
        /// default until the Skill's frontmatter overrides at the server side.
        /// </summary>
        private static void DefaultsForScenario(int idx,
            out bool allowParams, out bool allowComp,
            out bool allowScript, out int componentScope)
        {
            switch (idx)
            {
                case 0: // Inspect — read only
                    allowParams = false; allowComp = false; allowScript = false; componentScope = 0; return;
                case 1: // Tune — params only
                    allowParams = true; allowComp = false; allowScript = false; componentScope = 0; return;
                case 2: // Coach — curated, no scripting
                    allowParams = true; allowComp = true; allowScript = false; componentScope = 0; return;
                case 3: // Execute — Skill-defined; default to Coach behaviour
                    allowParams = true; allowComp = true; allowScript = false; componentScope = 0; return;
                case 4: // Author — full
                    allowParams = true; allowComp = true; allowScript = true; componentScope = 1; return;
                default:
                    allowParams = true; allowComp = true; allowScript = false; componentScope = 0; return;
            }
        }

        private static bool ResolveBoolOverride(int ov, bool defaultValue)
        {
            if (ov == 0) return false;
            if (ov == 1) return true;
            return defaultValue;
        }
    }
}
