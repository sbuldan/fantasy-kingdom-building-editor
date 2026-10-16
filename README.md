# Fantasy Kingdom Building Editor

Custom Unity editor for building modular House and Castle structures with separately licensed POLYGON Fantasy Kingdom prefabs.

This repository contains only the custom builder and floor metadata component. No purchased models, textures, materials, prefabs, scenes, or Unity project settings are included.

## Requirements and installation

Developed and compile-checked with Unity **6000.4.0f1**. Other versions have not been verified; the tool uses Unity 6 APIs including GetEntityId().

Obtain your own licensed POLYGON Fantasy Kingdom asset pack. The builder expects prefabs at:

- Assets/PolygonFantasyKingdom/Prefabs/House
- Assets/PolygonFantasyKingdom/Prefabs/Castle

Adjust HouseFolder and CastleFolder in the editor source if your pack uses different paths.

Copy the included Assets/Editor/FantasyKingdomHouseBuilder.cs and Assets/Scripts/HouseBuilderFloorMarker.cs, together with their .meta files, into your Unity project at the same paths. The floor marker must remain outside an Editor folder.

After compilation, open **Tools > Fantasy Kingdom > House Builder**.

## Keyboard controls

Use shortcuts with the Scene view focused and the builder window open.

| Key | Build Mode on | Build Mode off |
| --- | --- | --- |
| U | Disable builder | Enable builder |
| A / S | Rotate preview -90 / +90 degrees | Rotate selected module around rendered center |
| Z / X | Move down / up one floor | Unity default |
| [ / ] | Cycle placement prefab | Swap selected module at its pivot |
| P | Sample prefab under cursor | Sample prefab under cursor |
| E | Erase module under cursor | Activate Unity Rotate tool |
| Q / W / R / T | Exit builder and use Unity tool | Unity default |
| Shift + left click | Erase module | Unity default |

Ctrl/Cmd/Alt combinations are ignored by builder shortcuts.

Thumbnail clicks select the placement prefab while building. With Build Mode off, they replace a selected kit prefab, preserving its pivot transform, parent, and hierarchy order.

## Placement and floors

- Default grid: **1.25 m**; full-module option: **2.5 m**.
- Default floor height: **2.5 m**.
- Floor Offset provides additional elevation.
- Optional bottom anchoring affects placement and live mesh previews.
- House/Castle visibility filters, categories, search, and thumbnails organize the palette.
- Horizontal guides and a vertical ruler show the active placement height.
- New modules receive floor metadata and groups below HOUSE BUILD.
- Isolate Current Floor and Show All Floors control Scene-view visibility.
- Tag Selected to Current Floor assigns an existing module without changing its world position.
- Empty floor groups are cleaned up while the builder window is open.
- Placement, rotation, swapping, erasing, and tagging support Undo.

## Asset licensing and repository scope

POLYGON Fantasy Kingdom is a third-party Synty Studios asset pack. This is an independent custom editor; users must supply their own licensed assets. No rights to third-party content are granted here.

The .gitignore explicitly allows only the two custom scripts, their .meta files, and repository documentation/configuration. Other Unity content, the old shop scene builder, and the analysis utility are excluded.
