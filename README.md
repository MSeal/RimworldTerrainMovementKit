# RimworldTerrainMovementKit

![Version](https://img.shields.io/badge/Rimworld-1.1-brightgreen.svg) on [Steam](https://steamcommunity.com/sharedfiles/filedetails/?id=2048567351)

![Alt text](About/Preview.png?raw=true "TMK")

A mod for other mods to add terrain specific movement options and restrictions

## Description

This mod is for other mods to use, it has no direct benefit to players without writing additional rules for terrain / pawns based.

What the mod provides is a toolkit for adding rules and statistics for managing movement on various terrain types to the game. It makes pawns respect new Speed baseStats for both moving through a type of terrain and for planning pathing through that terrain. It also allows you to specify new pathCost stats to pair with Speed stats in order for pawns with the stat to move at a different speed than pawns without the stat. And finally it adds rules such that pawns can be restricted to certain terrain, or not able to enter other terrain.

### What's an example of using this mod?

The [SwimmingKit](https://steamcommunity.com/sharedfiles/filedetails/?id=1542399915) mod uses this mod to enable SwimSpeed to affect movement in water. It also add the aquatic tag in a mod extension to make pawns only able to traverse water tiles. This is being used in the [Biomes!](https://steamcommunity.com/sharedfiles/filedetails/?id=2038001322) to support Sharks, Turtles, and other semi-aquatic or fully aquatic animals.

For modded tile types you could also add movement rules. Say you have a lava tile. You can add a Lava Monster who can only move on lava. Then add some special tech armor that lets your pawns walk on lava to go battle it.

## How to Use

See the [TMK wiki](https://github.com/MSeal/RimworldTerrainMovementKit/wiki) for details on how to add these rules to your mod.

## How to Add to your Mod

The mod can be used as a mod dependency OR as a [direct DLL](https://github.com/MSeal/RimworldTerrainMovementKit/releases) in your mod assemblies. If you use the second option, be advised that you should add the following to your About.xml to help ensure multiple mods using the same kit will load the latest version.

```xml
<ModMetaData>
    ...
    <loadAfter>
      <li>pyrce.terrain.movement.modkit</li>
      <li>BiomesTeam.BiomesCore</li>
    </loadAfter>
    ...
```

If the mods above are not present it will still check for mod load orders of this library DLL and emit an error message (but still load) if it detects a later version lower in the users mod list.

## Credits

The mod Logo above used the following free assets:

Lava: ["LuminousDragonGames"](https://opengameart.org/content/2-seamless-lava-tiles)
Rock: ["Para"](https://opengameart.org/content/weathered-rock-pack)
Sand: ["txturs"](https://opengameart.org/content/2048-digitally-painted-tileable-desert-sand-texture)
Water: ["Aswin Vos"](https://opengameart.org/content/water)
