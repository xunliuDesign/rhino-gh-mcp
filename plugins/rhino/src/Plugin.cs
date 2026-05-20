using System;
using Rhino;
using Rhino.PlugIns;

namespace RhinoGhMcp.Rhino
{
    /// <summary>
    /// rhino-gh-mcp Rhino plugin. Hosts the TCP-JSON MCP listener for the
    /// rhino_* tool surface. Companion to the Python MCP server in /server/
    /// and the Grasshopper plugin in /plugins/grasshopper/.
    ///
    /// On load, this plugin does NOT start the listener automatically — run
    /// the `_ToggleMcpService` command inside Rhino to flip it on.
    /// </summary>
    public class RhinoGhMcpRhinoPlugin : PlugIn
    {
        public RhinoGhMcpRhinoPlugin()
        {
            Instance = this;
        }

        public static RhinoGhMcpRhinoPlugin Instance { get; private set; }

        protected override LoadReturnCode OnLoad(ref string errorMessage)
        {
            RhinoApp.WriteLine(
                "rhino-gh-mcp Rhino plugin loaded. " +
                "Run `_ToggleMcpService` to start/stop the listener on 127.0.0.1:9876."
            );
            return LoadReturnCode.Success;
        }

        protected override void OnShutdown()
        {
            try { McpService.Instance.Stop(); } catch { /* best effort */ }
        }
    }
}
