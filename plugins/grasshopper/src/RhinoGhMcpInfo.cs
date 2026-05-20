using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace RhinoGhMcp
{
    /// <summary>
    /// Assembly metadata for the rhino-gh-mcp Grasshopper plugin.
    /// </summary>
    public class RhinoGhMcpInfo : GH_AssemblyInfo
    {
        public override string Name => "rhino-gh-mcp";

        public override Bitmap Icon => null;

        public override string Description =>
            "MCP server bridge: lets an LLM client read and modify this Grasshopper canvas " +
            "through a loopback HTTP listener. Companion to the rhino-gh-mcp Python server.";

        // Fresh GUID for v1 — distinct from the v0 archive AssemblyInfo so both
        // can coexist on the same machine during transition.
        public override Guid Id => new Guid("9144e6c3-0a55-439a-96ff-25d2e567cfd5");

        public override string AuthorName => "Xun Liu";

        public override string AuthorContact => "https://github.com/xunliuDesign/rhino-gh-mcp";

        public override string AssemblyVersion => GetType().Assembly.GetName().Version.ToString();
    }
}
