"""Copies the Synty POLYGON scenery the painter's palette uses out of the purchased packs.

Synty source files may not be redistributed, so assets/synty/ is gitignored and rebuilt from
your own copy of the packs (the meshes data/biomes.json names, on the atlases it names):

    python tools/fetch_synty.py [--packs D:/dev/puchased-assets] [--missing]

run.cmd runs it with --missing on every launch, so meshes added here (or by a pulled commit)
arrive without rebuilding the folder, and imports them when any came.
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
    # Camps.
    "SM_Prop_Skull_01", "SM_Prop_Fence_01", "SM_Prop_Fence_02",
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
    # Camps.
    "SM_Env_CampFire_01", "SM_Bld_Hut_01", "SM_Prop_Cart_01", "SM_Prop_Cart_02", "SM_Prop_Cart_03",
    "SM_Prop_Logpile_01", "SM_Prop_Loghalf_01", "SM_Prop_Basket_01",
    "SM_Prop_Sack_01", "SM_Prop_Sack_02", "SM_Prop_Sack_03",
    "SM_Bld_Fence_01", "SM_Bld_Fence_02", "SM_Bld_FencePost_01", "SM_Bld_Village_HangingCloth_01",
    "SM_Prop_Crate_01", "SM_Prop_Barrel_01", "SM_Prop_Stall_Table_01",
    # Taverns: an inn, its walled yard and the casks; the snowed-in lodge.
    "SM_Bld_Village_01", "SM_Bld_Village_06", "SM_Bld_Wall_01", "SM_Bld_Wall_02",
    "SM_Bld_Hut_01_Snow", "SM_Bld_Fence_01_Snow",
    "SM_Prop_Barrel_02", "SM_Prop_RoadSign_01", "SM_Prop_Chest_01", "SM_Prop_Crate_02",
    "SM_Prop_Pot_01",
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
    # Camps: the goblins' tents, palisades and spits, and the junk any camp gathers.
    *(f"SM_Prop_Goblin_Tent_0{i}" for i in range(1, 4)),
    "SM_Prop_Goblin_Fence_01", "SM_Prop_Goblin_Fence_02", "SM_Prop_Goblin_Tower_01",
    *(f"SM_Prop_Goblin_Spikes_0{i}" for i in range(1, 5)),
    "SM_Prop_Goblin_Rotisserie_01", "SM_Prop_Goblin_Rotisserie_Meat_01", "SM_Prop_Goblin_Drum_01",
    *(f"SM_Prop_Goblin_Branch_0{i}" for i in range(1, 4)),
    "SM_Prop_Goblin_Meat_01", "SM_Prop_Bonfire_01",
    "SM_Prop_Log_Spike_01", "SM_Prop_Log_Spike_05", "SM_Prop_Log_Spike_Large_01",
    "SM_Prop_Barrel_01", "SM_Prop_Barrel_Broken_01", "SM_Prop_Barrel_Broken_02",
    "SM_Prop_Crate_Wood_01", "SM_Prop_Crate_Wood_02", "SM_Prop_Crate_Wood_03",
    "SM_Prop_Stool_01", "SM_Prop_Stool_02", "SM_Prop_Plank_01", "SM_Prop_Plank_02",
    *(f"SM_Env_Rubble_Plank_0{i}" for i in range(1, 4)),
    "SM_Prop_Bench_01", "SM_Prop_Cauldron_01", "SM_Prop_TorchStick_01", "SM_Prop_Table_01",
    "SM_Wep_Banner_02", "SM_Wep_Banner_03",  # (Banner_01's cloth does not draw on the ARC shader)
    "SM_Prop_Skeleton_Cage_01", "SM_Env_Wood_Platform_01", "SM_Prop_Bed_01",
    # Taverns: kegs on their stands, a cask the size of a shed, tables and stools.
    "SM_Prop_Barrel_02", "SM_Prop_Barrel_03", "SM_Prop_Barrel_Large_01",
    "SM_Prop_Table_Round_01", "SM_Prop_Table_Round_Broken_01", "SM_Prop_Stool_03",
    "SM_Prop_Candle_Stand_01", "SM_Prop_Crate_Wood_04",
    # Defences: a round tower drum and its stair, square and round tower posts, a gatehouse and its
    # portcullis, iron spike fences, stakes, a lookout's ladder and deck; and the mage's crystals,
    # orb lamps, braziers and books.
    "SM_Env_Wall_Round_01", "SM_Env_SpiralStairs_02",
    "SM_Env_Wall_Pillar_Large_01", "SM_Env_Wall_Pillar_Large_Round_01", "SM_Env_Wall_Pillar_Large_Round_01_Base",
    "SM_Env_Wall_DoorFrame_Double_Round_01", "SM_Env_Portcullis_01",
    "SM_Env_SpikeFence_01", "SM_Env_SpikeFence_02",
    "SM_Prop_Log_Spike_02", "SM_Prop_Log_Spike_03", "SM_Prop_Log_Spike_Large_02",
    "SM_Prop_Ladder_01", "SM_Env_Wood_Platform_02",
    "SM_Prop_Tech_Crystal_01", "SM_Env_GlowingOrb_03", "SM_Env_Gem_Spike_01", "SM_Env_Gem_Spike_02",
    "SM_Prop_Brazier_01", "SM_Prop_Bookcase_01", "SM_Item_BookPile_01",
    # The dungeon biomes (crypt, warrens, caverns, forge, halls): their scatter and the meshes their
    # hundred set pieces use. (Not the Wall_Banner_*, which hang below their pivot and so sink under
    # the floor, nor Ground_Pit or CrackedRock_01, which draw as one big flat slab over a piece.)
    "SM_Env_Alter_01", "SM_Env_BonePile_Small_01", "SM_Env_BonePile_Small_02", "SM_Env_Bone_Horn_01", "SM_Env_Bone_Rib_02",
    "SM_Env_Bone_Skull_Jaw_01", "SM_Env_Bone_Spike_01", "SM_Env_Bone_Spike_02", "SM_Env_Bone_Spike_03", "SM_Env_Bone_Spike_04",
    "SM_Env_Bone_Spike_05", "SM_Env_Bone_Spine_01", "SM_Env_Bone_Spine_02", "SM_Env_DoorDouble_Round_01", "SM_Env_DoorDouble_Round_02",
    "SM_Env_Door_Bars_01", "SM_Env_Door_Frame_02", "SM_Env_Door_Large_Frame_01", "SM_Env_Door_Large_Stone_01", "SM_Env_Door_Large_Stone_02",
    "SM_Env_Entrance_Crypt_01", "SM_Env_Flowers_01", "SM_Env_Flowers_02", "SM_Env_Gem_Large_01", "SM_Env_Gem_Large_02",
    "SM_Env_GlowingOrb_01", "SM_Env_GlowingOrb_02", "SM_Env_GlowingOrb_04", "SM_Env_GlowingOrb_05", "SM_Env_Grass_01",
    "SM_Env_Grass_02", "SM_Env_Grass_03", "SM_Env_Minetrack_Broken_01", "SM_Env_Minetrack_Broken_02", "SM_Env_Minetrack_Straight_01",
    "SM_Env_Moss_Patch_01", "SM_Env_Mushroom_Giant_01", "SM_Env_Mushroom_Giant_02", "SM_Env_Mushroom_Giant_03", "SM_Env_Mushroom_Giant_04",
    "SM_Env_Mushroom_Giant_05", "SM_Env_Mushroom_Small_01", "SM_Env_Mushroom_Small_02", "SM_Env_Mushroom_Small_03", "SM_Env_Mushroom_Small_04",
    "SM_Env_Obelisk_01", "SM_Env_Obelisk_02", "SM_Env_Pillar_Round_02", "SM_Env_Pillar_Round_03", "SM_Env_Pillar_Round_Simple_02",
    "SM_Env_Pillar_Square_04", "SM_Env_Pillar_Square_07", "SM_Env_Railing_03", "SM_Env_Railing_Broken_02", "SM_Env_RockPile_Pillar_01",
    "SM_Env_RockPile_Pillar_02", "SM_Env_RockPile_Pillar_03", "SM_Env_RockPile_Rounded_01", "SM_Env_RockPile_Rounded_02", "SM_Env_RockPile_Rounded_03",
    "SM_Env_RockPile_Square_01", "SM_Env_RockPile_Square_02", "SM_Env_RockPile_Square_03", "SM_Env_RockPile_Square_04", "SM_Env_RockPile_Square_05",
    "SM_Env_RockPile_Square_06", "SM_Env_Rock_Flat_02", "SM_Env_Rock_Flat_04", "SM_Env_Rock_Flat_05", "SM_Env_Rock_Flat_06",
    "SM_Env_Rock_Flat_Large_01", "SM_Env_Rock_Flat_Large_02", "SM_Env_Rock_Flat_Large_03", "SM_Env_Rock_Pebble_01", "SM_Env_Rock_Pebble_02",
    "SM_Env_Rock_Pebble_03", "SM_Env_Rock_Pillar_01", "SM_Env_Rock_Pillar_02", "SM_Env_Rock_Pillar_03", "SM_Env_Rock_Rectangle_01",
    "SM_Env_Rock_Rectangle_02", "SM_Env_Rock_Rectangle_03", "SM_Env_Rock_Rectangle_05", "SM_Env_Rock_Rectangle_07", "SM_Env_Rock_Round_01",
    "SM_Env_Rock_Round_02", "SM_Env_Rock_Round_03", "SM_Env_Rock_Round_04", "SM_Env_Roots_01", "SM_Env_Roots_02",
    "SM_Env_Rubble_Pebbles_01", "SM_Env_Rubble_Pebbles_02", "SM_Env_Rubble_Pebbles_03", "SM_Env_Rune_Pillar_03", "SM_Env_Rune_Pillar_04",
    "SM_Env_Rune_Pillar_05", "SM_Env_Rune_Rounded_01", "SM_Env_Rune_Rounded_02", "SM_Env_Rune_Rounded_03", "SM_Env_Rune_Rounded_04",
    "SM_Env_Rune_Rounded_05", "SM_Env_Rune_Square_01", "SM_Env_Rune_Square_02", "SM_Env_Rune_Square_03", "SM_Env_Rune_Square_04",
    "SM_Env_Rune_Square_05", "SM_Env_Stairs_Large_01", "SM_Env_Stalagmite_01", "SM_Env_Stalagmite_02", "SM_Env_Stalagmite_03",
    "SM_Env_Stalagmite_04", "SM_Env_Stalagmite_05", "SM_Env_Stalagmite_06", "SM_Env_Stalagmite_07", "SM_Env_Stone_Throne_01",
    "SM_Env_Stone_Vessel_01", "SM_Env_Tiles_Pentagram_01", "SM_Env_Trap_Hatch_01", "SM_Env_Tree_Dandelion_01", "SM_Env_Tree_Dandelion_02",
    "SM_Env_Tree_Dandelion_03", "SM_Env_Tree_Twisted_01", "SM_Env_Vine_Spikes_01", "SM_Env_Vine_Spikes_02", "SM_Env_Volcanic_01",
    "SM_Env_Wall_Alcove_Small_01", "SM_Env_Wood_Construction_Wheel_02", "SM_Env_Wood_Construction_Wheel_04", "SM_Env_Wood_Pole_02", "SM_Item_Book_01",
    "SM_Item_Coins_01", "SM_Item_Coins_02", "SM_Item_Coins_03", "SM_Item_Coins_04", "SM_Item_Inkwell_01",
    "SM_Item_Jar_01", "SM_Item_Jar_02", "SM_Item_Potion_01", "SM_Item_Potion_02", "SM_Item_Potion_03",
    "SM_Item_Potion_04", "SM_Item_Potion_05", "SM_Item_Potion_06", "SM_Item_Potion_07", "SM_Item_Potion_08",
    "SM_Item_Potion_09", "SM_Item_Potion_Pole_01", "SM_Item_Scale_01", "SM_Item_Vial_01", "SM_Item_Vial_03",
    "SM_Prop_Bolt_01", "SM_Prop_Bookcase_02", "SM_Prop_Bookcase_03", "SM_Prop_Brick_01", "SM_Prop_Brick_02",
    "SM_Prop_Candle_01", "SM_Prop_Candles_01", "SM_Prop_Candles_02", "SM_Prop_Candles_03", "SM_Prop_Candles_04",
    "SM_Prop_Chest_01", "SM_Prop_Chest_01_Lid", "SM_Prop_Chest_02", "SM_Prop_Chest_02_Lid", "SM_Prop_Chest_03",
    "SM_Prop_Chest_03_Lid", "SM_Prop_Chest_04", "SM_Prop_Chest_Wood_01", "SM_Prop_Coffin_01", "SM_Prop_Coffin_Lid_01",
    "SM_Prop_Crate_Metal_01", "SM_Prop_Crate_Metal_02", "SM_Prop_Crate_Metal_03", "SM_Prop_Crate_Metal_Open_01", "SM_Prop_Crate_Ornate_01",
    "SM_Prop_Crate_Rocks_01", "SM_Prop_Crate_Wood_05", "SM_Prop_Fireplace_01", "SM_Prop_Forge_FirePit_01", "SM_Prop_Forge_Ventilation_Pipe_01",
    "SM_Prop_Gem_01", "SM_Prop_Gem_02", "SM_Prop_Gem_03", "SM_Prop_Gem_04", "SM_Prop_Gem_05",
    "SM_Prop_Jewel_01", "SM_Prop_Jewel_06", "SM_Prop_Lantern_01", "SM_Prop_Lantern_02", "SM_Prop_Metal_Fence_01",
    "SM_Prop_Metal_Fence_Post_01", "SM_Prop_Metal_Fence_Weak_01", "SM_Prop_Minecart_01", "SM_Prop_Minecart_Wheel_01", "SM_Prop_Minecart_Wheel_02",
    "SM_Prop_Rug_01", "SM_Prop_Rug_02", "SM_Prop_Skeleton_01", "SM_Prop_Skeleton_Knight_Throne_01", "SM_Prop_Skeleton_Slave_Lying_01",
    "SM_Prop_Skeleton_Slave_Shackles_01", "SM_Prop_Skeleton_Slave_Shackles_02", "SM_Prop_Skeleton_Slave_Wall_Sitting_01", "SM_Prop_Skeleton_Table_01", "SM_Prop_Skeleton_Table_02",
    "SM_Prop_StoneChair_01", "SM_Prop_StoneTable_01", "SM_Prop_Tech_Chamber_01", "SM_Prop_Tech_Chamber_Broken_01", "SM_Prop_Tech_Cog_01",
    "SM_Prop_Tech_Cog_02", "SM_Prop_Tech_Cog_03", "SM_Prop_Tech_Cog_04", "SM_Prop_Tech_Conveyor_01", "SM_Prop_Tech_Engine_01",
    "SM_Prop_Tech_Lever_01", "SM_Prop_Tech_Pipe_01", "SM_Prop_Tech_Pipe_02", "SM_Prop_Tech_Pole_01", "SM_Prop_Tech_Poles_01",
    "SM_Prop_Tech_SwitchBoard_01", "SM_Prop_Tech_Turbine_01", "SM_Prop_Toture_Cage_01", "SM_Prop_Toture_IronMaiden_01", "SM_Prop_Toture_IronMaiden_Door_L",
    "SM_Prop_Toture_Stocks_01", "SM_Prop_Toture_StretchTable_01", "SM_Prop_Trap_Pit_01", "SM_Prop_Trap_Pit_Door_L", "SM_Prop_Trap_Pit_Door_R",
    "SM_Prop_Trap_Saw_02", "SM_Prop_Trap_Saw_Rail_01", "SM_Prop_Trap_Spears_01", "SM_Prop_Trap_Spears_Grate_01", "SM_Prop_Trap_Spikes_01",
    "SM_Prop_Vase_01", "SM_Prop_Vase_02", "SM_Prop_Vase_03", "SM_Prop_Vase_04", "SM_Prop_Vase_05",
    "SM_Prop_Vase_06", "SM_Prop_Vase_07", "SM_Prop_Vase_08", "SM_Prop_Vase_09", "SM_Prop_Vase_Group_01",
    "SM_Prop_Vase_Shard_01", "SM_Prop_Vase_Shard_04", "SM_Prop_WeaponRack_01", "SM_Wep_Banner_04", "SM_Wep_Banner_05",
    "SM_Wep_Cutlass_01", "SM_Wep_Goblin_Axe_Large_01", "SM_Wep_Goblin_Club_01", "SM_Wep_Goblin_Gem_Hammer_01", "SM_Wep_Goblin_Halberd_01",
    "SM_Wep_Goblin_Mace_01", "SM_Wep_Goblin_Shiv_Stone_01", "SM_Wep_Goblin_Spear_01", "SM_Wep_Goblin_Staff_01", "SM_Wep_GreatSword_01",
    "SM_Wep_Greatsword_Curved_01", "SM_Wep_Hammer_Large_Metal_01", "SM_Wep_Hammer_Small_01", "SM_Wep_Hammer_Small_02", "SM_Wep_HandAxe_01",
    "SM_Wep_Ornate_Axe_02", "SM_Wep_Ornate_Sword_02", "SM_Wep_Shield_Gravestone_01", "SM_Wep_Shield_Heater_01", "SM_Wep_Shield_Heater_02",
    "SM_Wep_Shield_Ornate_01", "SM_Wep_Shield_Ornate_02", "SM_Wep_Shield_Plank_01", "SM_Wep_Shield_Round_01", "SM_Wep_Shield_Skull_01",
    "SM_Wep_Shield_Spike_01", "SM_Wep_Spear_01", "SM_Wep_Spear_02", "SM_Wep_Straightsword_01",
    # The themed pieces: the battlefield's fallen weapons and arrows, the burning's beams.
    "SM_Wep_Axe_01", "SM_Wep_Banner_01", "SM_Wep_BrokenSword_01", "SM_Wep_Greatsword_Straight_01",
    "SM_Wep_Ornate_Spear_01", "SM_item_Arrow_01", "SM_Prop_Log_Spike_04",
    "SM_Env_ReinforedPole_01", "SM_Env_Wood_Pole_01",
    # The second hundred: hoards, markets, cults, pilgrims, roots and the rest.
    "SM_Env_Bone_Cracked_02", "SM_Env_Bone_Rib_01", "SM_Env_Bone_Spine_03", "SM_Env_Bone_Tail_01",
    "SM_Env_Grate_Ground_01", "SM_Env_Railing_02", "SM_Env_ReinforedPole_02",
    "SM_Env_Rock_Flat_Platform_02", "SM_Env_Rock_Flat_Platform_03", "SM_Env_Stairs_02", "SM_Env_Tiles_Rune_02",
    "SM_Env_Tree_Twisted_02", "SM_Env_Wall_Alcove_01", "SM_Env_Wall_Fireplace_01", "SM_Env_Wood_Bridge_02",
    "SM_Env_Wood_Construction_02", "SM_Item_Vial_02", "SM_Prop_Bed_02",
    "SM_Prop_Chain_01", "SM_Prop_Chain_02", "SM_Prop_Chain_03", "SM_Prop_Chain_05",
    "SM_Prop_Chest_04_Lid", "SM_Prop_Chest_Wood_Lid", "SM_Prop_Jewel_02", "SM_Prop_Jewel_03",
    "SM_Prop_Table_Round_02", "SM_Prop_Trap_WallArrow_Hole_01", "SM_Prop_Trap_WallArrow_Hole_02",
    "SM_Wep_Crystal_Axe_Large_01", "SM_Wep_Goblin_Axe_01", "SM_Wep_Goblin_Machete_01",
    "SM_Wep_Ornate_GreatAxe_01", "SM_Wep_Staff_Gem_01",
    # The showcase's own (src/Showcase): the torches on its corridor walls.
    "SM_Prop_Torch_Ornate_01",
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
    parser.add_argument("--missing", action="store_true",
                        help="copy only files not in assets/synty yet (run.cmd does this each launch)")
    args = parser.parse_args()

    archives: dict[str, zipfile.ZipFile] = {}
    for name, inner, dest in FILES:
        target = OUT / dest
        if args.missing and target.exists():
            continue
        archive = archives.get(name) or archives.setdefault(name, zipfile.ZipFile(args.packs / name))
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(archive.read(inner))
        print(f"{dest}")


if __name__ == "__main__":
    main()
