using System.Runtime.InteropServices;
using Rhino.PlugIns;

// Stable Rhino plugin GUID — registered with Rhino on first install, persists
// across rebuilds. If you change this, every user has to re-register the
// plugin via _PluginManager.
[assembly: Guid("3f88bb55-3368-4204-9d0a-55911c9349ee")]

// Plugin metadata surfaced by _PluginManager.
[assembly: PlugInDescription(DescriptionType.Address, "")]
[assembly: PlugInDescription(DescriptionType.Country, "")]
[assembly: PlugInDescription(DescriptionType.Email, "")]
[assembly: PlugInDescription(DescriptionType.Phone, "")]
[assembly: PlugInDescription(DescriptionType.Fax, "")]
[assembly: PlugInDescription(DescriptionType.Organization, "Xun Liu")]
[assembly: PlugInDescription(DescriptionType.UpdateUrl, "https://github.com/xunliuDesign/rhino-gh-mcp")]
[assembly: PlugInDescription(DescriptionType.WebSite, "https://github.com/xunliuDesign/rhino-gh-mcp")]
[assembly: PlugInDescription(DescriptionType.Icon, "RhinoGhMcp.Rhino.EmbeddedResources.plugin-utility.ico")]
