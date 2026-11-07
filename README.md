# ARC Prefabs

The set piece painter, split out of Rogue Guys: hand-made layouts of Synty props that a game's
field drops into its biome regions. It paints on a region of one biome with no scatter, with
the game's camera and ARC shaders, so a piece looks here as it will in a run.

```
run.cmd                        # a new piece in the home biome
run.cmd --biome=frost          # a new piece in Rimefrost
run.cmd grove                  # open data/setpieces/grove.json (other options may follow)
run.cmd --browse               # start on the Browse tab
run.cmd --showcase             # fly through a dungeon built from the pieces
```

Set `GODOT` to use another Godot 4.7 .NET editor and `PREFABS_NOBUILD=1` to skip the C# build.
`--seed=N` picks which map the regions come from.

## Data

The project has its own data and nothing points at a game:

- `data/biomes.json` lists the biomes, their ground and the props each may use (the palette).
  Five are overland (meadow, pinewood, fen, frost, cinder) and five are dungeon floors for the
  roguelike deep: `crypt` (Hollow Crypts), `warrens` (Goblin Warrens), `caverns` (Glimmer
  Caverns), `forge` (Molten Deep) and `halls` (Fallen Halls), bare stone with no grass.
- `data/setpieces/*.json` are the pieces. Their optional `tags` are lowercase words for finding
  them in Browse, their optional `lore` a line or two on who made the place and what
  happened there, to steer how it is dressed, and their optional `lights` the lights the
  author placed where no prop burns or glows (games ignore all three). Their optional `themes`
  name the stories they belong to (below). Saving writes `data/setpieces/name.json` here, and
  beside it `name.png`, a 1024×768 picture from the game camera's angle on the biome's
  ground, so tools without Godot can see what a piece looks like. `run.cmd --render` redraws
  every picture (`--render=name` one, `--render=a,b` several) and quits.
- `data/themes.json` lists the themes: stories that run over several rooms, so a run meets a few
  rooms of one in a row before moving on, such as the Forgotten Battlefield, the Frosted Veins
  (cold, ice and snow), the Great Burning (the aftermath of a fire), the Plague Year or Skrag's
  Warband. There are 18, each with 5 to 9 pieces, and a piece may be in several; its biome still
  sets its floor and walls. A theme can also dress its rooms: recolour the floor, tint the light and
  strew its own clutter. Games ignore themes for now. A game that uses them should place pieces of
  one theme near each other.

A game takes pieces by copying the JSON files into its own `data/setpieces` by hand. When a
game changes its biomes or props, copy its `biomes.json` here so the palette and ground match.

The Synty files may not be redistributed, so `assets/synty/` is gitignored; `run.cmd` builds
it on the first launch with `tools/fetch_synty.py` from your own packs
(`--packs`, default `D:/dev/puchased-assets`). Add a mesh there when `biomes.json` gains one.

## Painting

The left panel is the palette, pictures of the props `data/biomes.json` lists for that biome
(or for every biome; hover one for its name), so nothing off-atlas gets in. The right panel
holds the piece's name, its tags (commas or spaces between them), its themes (ids from
`data/themes.json`; hover the box for the list), its lore, New, Undo and Save.

WASD pans and the wheel zooms; click places, shift-drag paints, right-click or right-drag
erases, alt-click picks up a placed prop; Q/E turn, Z/X scale, R toggles a random turn and
scale after each placement, G snaps to half a metre; ctrl+Z undoes and ctrl+S saves. The cross
marks the piece's centre and the ring its clear circle.

Tab (or the Select button) switches to selecting placed props. Click a prop anywhere on it,
shift-click to add or drop one, or drag a box over open ground to take every prop whose centre
is inside (a wall and its hidden blockers together). Drag the selection to move it (G snaps),
Q/E turn it 15° (with shift 1°) and Z/X scale it; a group turns and scales about its middle.
Delete removes it, ctrl+D copies it a metre over, Esc drops it, and ctrl+Z undoes each step.
Selected props are outlined in blue.

## Browsing

The Browse tab (B, or the tab at the top; B or Esc goes back) covers the painter with every
saved piece as a card: its picture, biomes, prop count, clear radius, weight, themes, tags and lore. At
the side, a search box matches names, tags and lore, biome and theme filters add up (a piece in any chosen biome
or theme shows) and tag filters narrow (a piece needs every chosen tag); each filter shows how many
pieces it would leave. Click a card to open it in the painter (twice if the open piece is
unsaved).

Tags in use: kind (`camp`, `ruin`, `shrine`, `grove`, `remains`, `ritual`, `tavern`, `fort`,
`crypt`, `cavern`, `forge`, `mine`, `prison`, `trap`, `library`, `hall`, `battle`), what's in it
(`building`, `walls`, `pillars`, `paving`, `gate`, `statue`, `tents`, `bones`, `water`,
`goblins`, `graveyard`, `tower`, `palisade`, `arcane`, `tomb`, `candles`, `throne`, `treasure`,
`rocks`, `crystals`, `mushrooms`, `machinery`, `lava`) and shape (`ring`, laid out round a
centre). Reuse these before making new ones.

Walls (the Dungeon pack's `SM_Env_Wall_*`) need two things the painter does not do for you.
Obstacles in the game are circles, so a wall blocks nothing itself: line it with
`SM_Env_Brick_Rubble_05` (0.26 m, blocking 0.5), which hides in a wall's thickness, about every
0.8 m, leaving doorways open. And only a wall's front face has bricks, so pair it with a twin
turned round over the same span (the two broken edges are each other's mirror). A wall's pivot
is at one end, as is a paving tile's at a corner.

## Showcase

`run.cmd --showcase` shows the pieces off: an endless dungeon of walled rooms, one piece in the
middle of each with twice its span around it (the open floor a game would spawn monsters on,
strewn with the biome's loose clutter), joined by torch-lit corridors, with a camera gliding
just over the wall tops. Every room has two exits besides the way in: the camera picks one at
random, and the other leads down a short corridor to a shut door. Rooms come in runs of 3 to 5
of one theme, each holding a different piece of it; a card names the theme and its story as the
run begins. Each room's theme, biome, name and lore show at the foot of the screen.
Space pauses, Up and Down change speed, Right skips to the next room, H hides the text and Esc
quits. `--seed=N` picks another dungeon, `--biomes=crypt,forge` its biomes (the five dungeon
ones by default) and `--themes=battlefield,frosted_veins` its themes (all by default). `run.cmd --capture=N` photographs the first N rooms into `captures/` (a
minimised window that never takes focus) and quits.

ARC's shaders are unlit, so the showcase lights the dungeon itself: every light near the camera
is baked into a map over the ground that lit copies of the prop and floor shaders read
(`shaders/arc_lit.gdshaderinc`; the game's shaders are untouched). `data/lights.json` sets the
dark, the fade with distance, a soft fill per biome, and which props give light (braziers,
candles, torches, fires, lava, crystals, glowing orbs...), each with a glowing marker where its
light comes from. A piece with nothing that glows gets `lights` of its own in its JSON; the
painter keeps them when it saves but does not edit them yet.

## Code

`src/World/SetPiecePainter.cs` is the painter, `SetPieceBrowser.cs` its Browse tab and
`ThumbnailStudio.cs` the pictures; `src/Showcase` is the showcase. The field (`Field.cs`, `WorldMap.cs`, `Grass.cs`,
`GroundMarks.cs`), camera (`CameraRig.cs`), set piece format (`SetPieces.cs`), Synty loading
and ARC materials (`src/Render`) and `shaders/arc_*` started as copies of Rogue Guys'; when a
game changes how the ground or a piece looks or reads, bring the change here too.
