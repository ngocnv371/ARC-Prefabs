"""Copies the Synty POLYGON scenery the painter's palette uses out of the purchased packs.

Synty source files may not be redistributed, so assets/synty/ is gitignored and rebuilt from
your own copy of the packs (the meshes data/biomes.json names, on the atlases it names):

    python tools/fetch_synty.py [--packs D:/dev/puchased-assets]
"""

import argparse
import pathlib
import zipfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
OUT = ROOT / "assets" / "synty"

NATURE_MESHES = [
    *(f"SM_Tree_Round_0{i}" for i in range(1, 6)),
    "SM_Tree_Stump_01", "SM_Tree_Stump_02", "SM_Tree_Log_01",
    *(f"SM_Rock_0{i}" for i in range(1, 5)),
    "SM_Rock_Small_01", "SM_Rock_Small_02",
    *(f"SM_Plant_Mushrooms_0{i}" for i in range(1, 4)),
    *(f"SM_Plant_Bush_0{i}" for i in range(1, 4)),
    *(f"SM_Plant_Grass_0{i}" for i in range(1, 6)),
    "SM_Plant_01", "SM_Plant_02", "SM_Plant_03",
    # Biomes beyond the meadow (data/biomes.json): flowers, pine woods, the fen, the cinder waste.
    # Only meshes wholly on the PolygonNature_01 atlas: the pines, willows and dead trees with
    # alpha-masked leaf cards need textures the ARC shader does not take.
    "SM_Plant_Flowers_01", "SM_Plant_PurpleFlower_01",
    *(f"SM_Tree_PolyPine_0{i}" for i in range(1, 4)),
    "SM_Tree_Stump_03", "SM_Tree_Stump_04", "SM_Tree_Log_02",
    *(f"SM_Plant_Fern_0{i}" for i in range(1, 4)),
    *(f"SM_Plant_Mushrooms_0{i}" for i in range(4, 7)),
    "SM_Rock_Pile_01", "SM_Rock_Pile_02", "SM_Rock_Pile_03",
    "SM_Prop_Pillar_Broken_Moss_01", "SM_Prop_Pillar_Broken_Moss_02", "SM_Prop_Pillar_Arch_Broken_Moss_01",
    *(f"SM_Tree_Swamp_0{i}" for i in range(1, 5)),
    "SM_Tree_Swamp_Stump_01", "SM_Tree_Swamp_Stump_02", "SM_Swamp_Root_01", "SM_Swamp_Root_02",
    "SM_Plant_Reeds_01", "SM_Plant_Reeds_02",
    "SM_Plant_Lillypad_Large_01", "SM_Plant_Lillypad_Large_02", "SM_Plant_Lillypad_Small_01",
    *(f"SM_Terrain_Swamp_Growth_0{i}" for i in range(1, 4)),
    "SM_Tree_Pine_Dead_01",
    "SM_Terrain_DustPile_Long_01", "SM_Terrain_DustPile_Small_01", "SM_Terrain_DustPile_Small_02",
    "SM_Terrain_Rubble_Pebbles_01", "SM_Terrain_Rubble_Pebbles_02", "SM_Terrain_Rubble_Pebbles_03",
    "SM_Rock_Boulder_01",
    # Ruined cities: low field walls. (The pack's beige pillars and arches clash with the Dungeon
    # pack's grey stone, and its rock tiles are cliff-sized.)
    "SM_Prop_StoneWall_01", "SM_Prop_StoneWall_02", "SM_Prop_StoneWall_03",
]

# The snowfield and the trees the Nature pack only has with leaf cards, from the Adventure pack
# (all on PolyAdventureTexture_01).
ADVENTURE_MESHES = [
    *(f"SM_Env_TreePine_0{i}" for i in range(1, 5)),
    "SM_Env_TreeDead_01", "SM_Env_TreeDead_02",
    "SM_Env_TreePine_01_Snow", "SM_Env_TreePine_02_Snow", "SM_Env_TreePine_03_Snow",
    *(f"SM_Env_Tree_0{i}_Snow" for i in range(1, 5)),
    "SM_Env_TreeBirch_01_Snow", "SM_Env_TreeDead_02_Snow",
    "SM_Env_Rock_03_Snow", "SM_Env_Rock_04_Snow", "SM_Env_Rock_05_Snow",
    *(f"SM_Env_SnowPile_0{i}" for i in range(1, 4)),
    *(f"SM_Env_Ice_0{i}" for i in range(1, 4)),
    # Ruined cities: a well. (The pack's roads and floor tiles lie flat at ground level and
    # flicker against the field's ground.)
    "SM_Bld_Well_01",
]

# Bones and broken rock for the cinder waste, from the Dungeon pack (Dungeons_Texture_01).
DUNGEON_MESHES = [
    *(f"SM_Env_Rock_Spike_0{i}" for i in range(1, 6)),
    *(f"SM_Env_CrackedRock_0{i}" for i in range(1, 4)),
    *(f"SM_Env_BonePile_0{i}" for i in range(1, 4)),
    "SM_Env_Bone_Ribcage_01", "SM_Env_Bone_Skull_01",
    "SM_Env_Rune_Pillar_01", "SM_Env_Rune_Pillar_02",
    # Ruined cities: broken walls and pillars, statues, a fountain, rubble, 5 m paving tiles.
    "SM_Env_Wall_01", "SM_Env_Wall_Half_01", "SM_Env_Wall_Quarter_01",
    "SM_Env_Wall_Broken_Edge_01", "SM_Env_Wall_Broken_Edge_02",
    "SM_Env_Wall_Archway_01", "SM_Env_Wall_Window_01", "SM_Env_Wall_DoorFrame_Round_01",
    "SM_Env_Pillar_Round_01", "SM_Env_Pillar_Broken_01", "SM_Env_Pillar_Broken_02",
    "SM_Env_Pillar_Broken_Pile_01", "SM_Env_Pillar_Broken_Pile_02",
    *(f"SM_Env_Statue_0{i}" for i in range(1, 5)),
    "SM_Env_Stone_Fountain_01", "SM_Env_Stone_Lantern_01", "SM_Env_Stairs_Broken_01",
    *(f"SM_Env_Brick_Rubble_0{i}" for i in range(1, 7)),
    *(f"SM_Env_Rubble_0{i}" for i in range(1, 4)),
    *(f"SM_Prop_Bricks_0{i}" for i in range(1, 5)),
    *(f"SM_Prop_Vase_Broken_0{i}" for i in range(1, 4)),
    "SM_Env_Tiles_01", "SM_Env_Tiles_02", "SM_Env_Tiles_03",
]

# (zip, path inside the zip, destination under assets/synty)
FILES = [
    *(("POLYGON_Nature_Source_Files_v2.zip", f"Source Files/FBX/{m}.fbx", f"nature/{m}.fbx") for m in NATURE_MESHES),
    ("POLYGON_Nature_Source_Files_v2.zip", "Source Files/Textures/PolygonNature_01.png", "nature/PolygonNature_01.png"),
    *(("POLYGON_Adventure_Pack_SourceFiles_v4.zip", f"SourceFiles/FBX/{m}.fbx", f"adventure/{m}.fbx") for m in ADVENTURE_MESHES),
    ("POLYGON_Adventure_Pack_SourceFiles_v4.zip", "SourceFiles/Textures/PolyAdventureTexture_01.png", "adventure/PolyAdventureTexture_01.png"),
    *(("POLYGON_Dungeon_Pack_SourceFiles_v3.zip", f"SourceFiles/FBX/{m}.fbx", f"dungeon/{m}.fbx") for m in DUNGEON_MESHES),
    ("POLYGON_Dungeon_Pack_SourceFiles_v3.zip", "SourceFiles/Textures/Dungeons_Texture_01.png", "props/Dungeons_Texture_01.png"),
]


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--packs", default="D:/dev/puchased-assets", type=pathlib.Path)
    args = parser.parse_args()

    archives: dict[str, zipfile.ZipFile] = {}
    for name, inner, dest in FILES:
        archive = archives.get(name) or archives.setdefault(name, zipfile.ZipFile(args.packs / name))
        target = OUT / dest
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(archive.read(inner))
        print(f"{dest}")


if __name__ == "__main__":
    main()
