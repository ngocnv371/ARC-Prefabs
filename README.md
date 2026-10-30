# ARC Prefabs

The set piece painter, split out of Rogue Guys: hand-made layouts of Synty props that a game's
field drops into its biome regions. It paints on a region of one biome with no scatter, with
the game's camera and ARC shaders, so a piece looks here as it will in a run.

```
run.cmd                        # a new piece in the home biome
run.cmd --biome=frost          # a new piece in Rimefrost
run.cmd grove                  # open data/setpieces/grove.json (other options may follow)
```

Set `GODOT` to use another Godot 4.7 .NET editor and `PREFABS_NOBUILD=1` to skip the C# build.
`--seed=N` picks which map the regions come from.

## Data

The project has its own data and nothing points at a game:

- `data/biomes.json` lists the biomes, their ground and the props each may use (the palette).
- `data/setpieces/*.json` are the pieces. Saving writes `data/setpieces/name.json` here, and
  beside it `name.png`, a 1024×768 picture from the game camera's angle on the biome's
  ground, so tools without Godot can see what a piece looks like. `run.cmd --render` redraws
  every picture (`--render=name` one) and quits.

A game takes pieces by copying the JSON files into its own `data/setpieces` by hand. When a
game changes its biomes or props, copy its `biomes.json` here so the palette and ground match.

The Synty files may not be redistributed, so `assets/synty/` is gitignored; `run.cmd` builds
it on the first launch with `tools/fetch_synty.py` from your own packs
(`--packs`, default `D:/dev/puchased-assets`). Add a mesh there when `biomes.json` gains one.

## Painting

The left panel is the palette, pictures of the props `data/biomes.json` lists for that biome
(or for every biome; hover one for its name), so nothing off-atlas gets in. The right panel
holds the piece's name, New, Undo and Save, and below them the list of saved pieces.

WASD pans and the wheel zooms; click places, shift-drag paints, right-click or right-drag
erases, alt-click picks up a placed prop; Q/E turn, Z/X scale, R toggles a random turn and
scale after each placement, G snaps to half a metre; ctrl+Z undoes and ctrl+S saves. The cross
marks the piece's centre and the ring its clear circle.

Tab (or the Select button) switches to selecting placed props. Click a prop anywhere on it,
shift-click to add or drop one, or drag a box over open ground to take every prop whose centre
is inside (a wall and its hidden blockers together). Drag the selection to move it (G snaps),
Q/E turn it 15° (with shift 1°) and Z/X scale it; a group turns and scales about its middle.
Delete removes it, ctrl+D copies it a metre over, Esc drops it, and ctrl+Z undoes each step.
Selected props are outlined in blue. The list shows every piece by biome
with a thumbnail, prop count, clear radius and weight (B hides it); click one to open it
(twice if the open piece is unsaved).

Walls (the Dungeon pack's `SM_Env_Wall_*`) need two things the painter does not do for you.
Obstacles in the game are circles, so a wall blocks nothing itself: line it with
`SM_Env_Brick_Rubble_05` (0.26 m, blocking 0.5), which hides in a wall's thickness, about every
0.8 m, leaving doorways open. And only a wall's front face has bricks, so pair it with a twin
turned round over the same span (the two broken edges are each other's mirror). A wall's pivot
is at one end, as is a paving tile's at a corner.

## Code

`src/World/SetPiecePainter.cs` is the painter, `SetPieceBrowser.cs` its list and
`ThumbnailStudio.cs` the pictures. The field (`Field.cs`, `WorldMap.cs`, `Grass.cs`,
`GroundMarks.cs`), camera (`CameraRig.cs`), set piece format (`SetPieces.cs`), Synty loading
and ARC materials (`src/Render`) and `shaders/arc_*` started as copies of Rogue Guys'; when a
game changes how the ground or a piece looks or reads, bring the change here too.
