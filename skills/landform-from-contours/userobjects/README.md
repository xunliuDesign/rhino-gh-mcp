# Landform user-objects

This directory will hold the `.ghuser` files referenced by `SKILL.md`:

- `Terrain.ContoursIn.ghuser`
- `Terrain.PatchSurface.ghuser`
- `Terrain.MeshFromSurface.ghuser`
- `Terrain.SlopeMap.ghuser`
- `Terrain.AspectMap.ghuser`

For now, drop the existing user-objects from your previous landform definition
here (the ones referenced in `_archive project/prompt.md`). The Skill engages
them by name via `gh_add_component`.

Future: ship a build script that generates user-objects from canonical `.gh`
templates so they live in version control.
