using System;
using Rhino;
using Rhino.Commands;

namespace RhinoGhMcp.Rhino
{
    /// <summary>
    /// `_ToggleMcpService` — start/stop the rhino-gh-mcp TCP-JSON listener.
    /// Idempotent: running it once starts the service, running it again stops it.
    /// </summary>
    public class ToggleMcpServiceCommand : Command
    {
        public override string EnglishName => "ToggleMcpService";

        public override Guid Id => new Guid("ac08bec6-4e25-495e-a765-6e0ad489d833");

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var svc = McpService.Instance;
            if (svc.IsRunning)
            {
                svc.Stop();
                RhinoApp.WriteLine("rhino-gh-mcp: MCP service stopped.");
            }
            else
            {
                svc.Start();
                RhinoApp.WriteLine($"rhino-gh-mcp: MCP service running on 127.0.0.1:{McpService.Port}");
            }
            return Result.Success;
        }
    }
}
