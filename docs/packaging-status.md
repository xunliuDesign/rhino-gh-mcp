# Packaging status — v0.2.0

This page tracks the state of the two install channels we ship in
v0.2.0: the `.mcpb` Desktop Extension (Claude Desktop) and the Yak
packages (Rhino Package Manager).

## TL;DR

| Channel | Built | Tested locally | Published | Notes |
|---|---|---|---|---|
| `.mcpb` Desktop Extension | ✅ | ✅ structurally (manifest + imports) | ⏳ awaits release | Build with `scripts/build-mcpb.sh`. Smoke-tested by extracting and importing the bundled module. End-to-end install in Claude Desktop is a manual user-side step. |
| Grasshopper `.yak` | ✅ | ✅ `yak build` succeeds, package contents verified | ⏳ awaits `yak push` | Build with `scripts/build-yak.sh grasshopper`. Local install testable via `yak install --source dist/` on the user's machine. |
| Rhino `.yak` | ✅ | ✅ `yak build` succeeds, package contents verified | ⏳ awaits `yak push` | Build with `scripts/build-yak.sh rhino`. |

## What's in `dist/`

After running `scripts/build-mcpb.sh && scripts/build-yak.sh` from a
fresh release build:

```
dist/
├── rhino-gh-mcp-0.2.0.mcpb                           ~280 KB
├── rhinogh-mcp-grasshopper-0.2.0-rh8_0-any.yak      ~600 KB
└── rhinogh-mcp-rhino-0.1.1-rh8_0-any.yak            ~280 KB
```

The `rh8_0-any` distribution tag is auto-derived by `yak build` from
the version of the RhinoCommon / Grasshopper reference assemblies the
plugin was compiled against. `any` means architecture-agnostic
(.NET 7 IL — Yak picks the right `net7.0` vs `net7.0-windows` subdir at
install time).

## `.mcpb` (Claude Desktop Extension)

### Bundle layout (open the .mcpb with any zip tool to inspect)

```
rhino-gh-mcp-0.2.0.mcpb
├── manifest.json              MCPB v0.3 manifest
├── icon.png
├── server/
│   ├── bootstrap.py           First-launch dep installer + re-exec
│   ├── requirements.txt       Mirrors server/pyproject.toml
│   └── src/rhino_gh_mcp/      Pure-Python server package
└── skills/                    landform + ladybug-environmental + README
```

### Dependency strategy

The MCPB v0.3 `python` server type expects you to bundle deps. We can't
easily pre-vendor cross-platform native wheels (pillow, pydantic-core,
cryptography differ per OS + arch), so the bundle ships pure source
plus `requirements.txt`. The bootstrap launcher does a one-time
`pip install --target server/lib -r requirements.txt` on first launch.

Subsequent launches are offline and start in < 500 ms — the bootstrap
just verifies the install stamp exists, prepends `server/src` and
`server/lib` to `sys.path`, and `os.execvpe`s the real
`python -m rhino_gh_mcp`. Internet is only required on the very first
launch.

Prereq the user still needs: **Python 3.11+ on PATH**. The MCPB spec
v0.3 has no auto-Python-detection. v0.4 introduces a `uv` server type
that handles deps automatically; once Claude Desktop ships support for
it we can drop the bootstrap launcher.

### Manifest validation

There's no published JSON Schema for the MCPB v0.3 manifest — the spec
lives in [`MANIFEST.md`](https://github.com/modelcontextprotocol/mcpb/blob/main/MANIFEST.md)
as prose. The bundle was verified by:

1. `python3 -c "import json; json.load(open('manifest.json'))"` — parses.
2. The build script asserts `manifest.json["server"]["entry_point"]`
   resolves to an existing file inside the bundle.
3. The bundled `rhino_gh_mcp` package imports successfully under
   Python 3.12 (smoke-tested by extracting to a temp dir and `python
   -c "import rhino_gh_mcp; from rhino_gh_mcp.skills import list_skills;
   print(list_skills())"`).

If a schema/validator surfaces upstream, wire it into
`scripts/build-mcpb.sh` as step 5.

### Manual user-side testing the maintainer should do before release

1. On macOS:
   - Drop the `.mcpb` onto Claude Desktop's Extensions panel.
   - Confirm the user_config form renders all four fields with the
     correct defaults.
   - Watch the extension log (Help → Logs) on first run — should see
     the bootstrap output and then `rhino_gh_mcp` logging "Starting…".
   - Restart Claude Desktop, open a fresh chat, call `gh_status`.
2. Repeat on Windows.
3. Confirm the bundled skills land in the right place: with the
   extension installed, the LLM should be able to call `list_skills`
   and see both `landform` and `ladybug-environmental`.

### Publishing

Anthropic's [official desktop-extensions catalog](https://www.anthropic.com/news/desktop-extensions)
is the most discoverable home, if submissions open up. In the
meantime the `.mcpb` ships as an asset on the
[GitHub Releases page](https://github.com/xunliuDesign/rhino-gh-mcp/releases).
README points the user at the latest release.

## Yak packages

### What was built

Both Yak packages were built locally with `/Applications/Rhino 8.app/Contents/Resources/bin/yak build`
against the Release-mode plugin outputs. The `yak build` invocation
emits two warnings that are not fatal:

```
WARNING: Content version doesn't match manifest: '0.2.0+<sha>' != '0.2.0'
WARNING: Content name doesn't match manifest: 'RhinoGhMcp' != 'rhinogh-mcp-grasshopper'
```

Both are intentional / unavoidable:

- The version mismatch is because `dotnet` appends the git commit SHA to
  `AssemblyInformationalVersion` when building from a Git working tree.
  The manifest version (`0.2.0`) is the source of truth that ships in
  Package Manager.
- The name mismatch is because the assembly is named `RhinoGhMcp` /
  `RhinoGhMcpRhino` (legacy C# identifier rules) while the Yak package
  uses the kebab-case `rhinogh-mcp-grasshopper` / `rhinogh-mcp-rhino`
  (Yak naming rules: letters, numbers, dashes, underscores).

Both packages have been smoke-tested by `unzip -l` to confirm contents
and structure.

### Publishing — what the maintainer must do manually

```bash
YAK="/Applications/Rhino 8.app/Contents/Resources/bin/yak"

# One-time: log in. Opens a browser to authenticate against rhino3d.com.
"$YAK" login

# Push both packages.
"$YAK" push dist/rhinogh-mcp-grasshopper-0.2.0-rh8_0-any.yak
"$YAK" push dist/rhinogh-mcp-rhino-0.1.1-rh8_0-any.yak
```

`yak push` requires a McNeel / Rhino Accounts login the automation
agent doesn't have. After `push` succeeds, the packages appear under
`_PackageManager` → search "rhino-gh-mcp" within ~5 minutes.

If the maintainer wants to test the package on their machine before
pushing to the public server, they can install from local:

```bash
"$YAK" install --source dist/ rhinogh-mcp-grasshopper
"$YAK" install --source dist/ rhinogh-mcp-rhino
```

## Rebuilding from a fresh checkout

```bash
# 1. Rebuild both C# plugins in Release config.
(cd plugins/grasshopper && dotnet build -c Release)
(cd plugins/rhino && dotnet build -c Release)

# 2. Build the Yak packages.
./scripts/build-yak.sh        # --skip-build to reuse bin/Release/

# 3. Build the .mcpb. No dotnet needed.
./scripts/build-mcpb.sh

# 4. Sanity-check the Python server still passes its tests.
(cd server && uv run pytest -q)
```

All three artifacts land in `dist/`.
