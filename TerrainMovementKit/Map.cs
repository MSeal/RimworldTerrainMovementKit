using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using HarmonyLib;
using System.Linq;
using RimWorld.Planet;

namespace TerrainMovement
{
    // Patching these two methods saves a LOT of other patches, even though this has nothing to do with temperature
    [HarmonyPatch(typeof(TileTemperaturesComp), "SeasonAcceptableFor", new Type[] { typeof(int), typeof(ThingDef) })]
    public class TileTemperaturesComp_SeasonAcceptableFor_TerrainAwareHack
    {
        static void Postfix(ref bool __result, int tile, ThingDef animalRace)
        {
            if (__result && typeof(Pawn).IsAssignableFrom(animalRace.thingClass))
            {
                Map map = Current.Game.FindMap(tile);
                if (map != null)
                {
                    __result = map.ThingCanEnter(animalRace);
                }
            }
        }
    }

    [HarmonyPatch(typeof(TileTemperaturesComp), "OutdoorTemperatureAcceptableFor", new Type[] { typeof(int), typeof(ThingDef) })]
    public class TileTemperaturesComp_OutdoorTemperatureAcceptableFor_TerrainAwareHack
    {
        static void Postfix(ref bool __result, int tile, ThingDef animalRace)
        {
            if (__result && typeof(Pawn).IsAssignableFrom(animalRace.thingClass))
            {
                Map map = Current.Game.FindMap(tile);
                if (map != null)
                {
                    __result = map.ThingCanEnter(animalRace);
                }
            }
        }
    }

    // These patches are used by Map primarily
    [HarmonyPatch(typeof(Reachability), "CanReach", new Type[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(PathEndMode), typeof(TraverseParms) })]
    public class CanReachMoveCheck
    {
        static bool Prefix(ref bool __result, Map ___map, IntVec3 start, LocalTargetInfo dest, PathEndMode peMode, TraverseParms traverseParams)
        {
            if (!___map.FullTerrainCanReach(dest, traverseParams.pawn))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ReachabilityImmediate), "CanReachImmediate", new Type[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(Map), typeof(PathEndMode), typeof(Pawn) })]
    public class CanReachImmediateMoveCheck
    {
        static bool Prefix(ref bool __result, IntVec3 start, LocalTargetInfo target, Map map, PathEndMode peMode, Pawn pawn)
        {
            if (!map.FullTerrainCanReach(target, pawn))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(TerrainGrid), "ResetGrids", new Type[0])]
    public class TerrainGrid_ResetGrids_Patch
    {
        private static void Postfix(ref Map ___map)
        {
            // Reset our cache when the grid resets
            MapExtensions.EdgeTerrainLookup.Remove(___map);
            MapExtensions.PawnReachableLookup.Remove(___map);
        }
    }

    [HarmonyPatch(typeof(TerrainGrid), "RemoveTopLayer")]
    public class TerrainGrid_RemoveTopLayer_Patch
    {
        private static void Postfix(ref Map ___map, IntVec3 c, bool doLeavings)
        {
            if (c.x == 0 || c.x == ___map.Size.x - 1 || c.z == 0 || c.z == ___map.Size.z - 1)
            {
                // Reset our cache when the grid edge changes
                MapExtensions.EdgeTerrainLookup.Remove(___map);
            }
            ___map.UpdatePawnTerrainChecksAffected(c);
        }
    }

    [HarmonyPatch(typeof(TerrainGrid), "SetTerrain")]
    public class TerrainGrid_SetTerrain_Patch
    {
        private static void Postfix(ref Map ___map, IntVec3 c, TerrainDef newTerr)
        {
            if (c.x == 0 || c.x == ___map.Size.x - 1 || c.z == 0 || c.z == ___map.Size.z - 1)
            {
                // Reset our cache when the grid edge changes
                MapExtensions.EdgeTerrainLookup.Remove(___map);
            }
            ___map.UpdatePawnTerrainChecksAffected(c);
        }
    }

    public static class SiteGenStepUtilityExtended
    {
        public static bool TryFindRootToSpawnAroundRectOfInterest(out CellRect rectToDefend, out IntVec3 singleCellToSpawnNear, Map map)
        {
            singleCellToSpawnNear = IntVec3.Invalid;
            if (!MapGenerator.TryGetVar("RectOfInterest", out rectToDefend))
            {
                rectToDefend = CellRect.Empty;
                if (!RCellFinder.TryFindRandomCellNearTheCenterOfTheMapWith((IntVec3 x) => x.Standable(map) && !x.Fogged(map) && x.GetRoom(map).CellCount >= 225, map, out singleCellToSpawnNear))
                {
                    return false;
                }
            }
            return true;
        }

        public static bool TryFindSpawnCellAroundOrNear(CellRect around, IntVec3 near, Map map, PawnKindDef kind, out IntVec3 spawnCell)
        {
            if (near.IsValid)
            {
                if (!CellFinderExtended.TryFindRandomSpawnCellForPawnNear(near, map, kind, out spawnCell, 10))
                {
                    return false;
                }
            }
            else if (!CellFinder.TryFindRandomCellInsideWith(around.ExpandedBy(8), (IntVec3 x) => !around.Contains(x) && x.InBounds(map) && x.Standable(map) && !x.Fogged(map) && map.PawnKindCanEnter(kind), out spawnCell))
            {
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Map), "FinalizeInit", new Type[0])]
    public class Map_FinalizeInit_Patch
    {
        private static void Postfix(ref Map __instance)
        {
            __instance.ResetLookup();
        }
    }

    [HarmonyPatch(typeof(MapPawns), "DeRegisterPawn", new Type[] { typeof(Pawn) })]
    public class DeregisterPawnMap
    {
        static bool Prefix(Map ___map, Pawn p)
        {
            if (MapExtensions.PawnReachableLookup.TryGetValue(___map, out Dictionary<Pawn, HashSet<int>> pawnLookup))
            {
                if (pawnLookup.ContainsKey(p))
                {
                    pawnLookup.Remove(p);
                }
            }
            return true;
        }
    }

    public static class MapExtensions
    {
        public static Dictionary<Map, TerrainAwarePathFinder> PatherLookup = new Dictionary<Map, TerrainAwarePathFinder>();
        public static Dictionary<int, Map> TileLookup = new Dictionary<int, Map>();
        public static Dictionary<Map, Dictionary<TerrainDef, int>> EdgeTerrainLookup = new Dictionary<Map, Dictionary<TerrainDef, int>>();
        public static Dictionary<Map, Dictionary<Pawn, HashSet<int>>> PawnReachableLookup = new Dictionary<Map, Dictionary<Pawn, HashSet<int>>>();
        public static void ResetLookup(this Map map)
        {
            // Always replace because tile ids get reused on game loads
            PatherLookup[map] = new TerrainAwarePathFinder(map);
            TileLookup[map.Tile] = map;
        }

        public static TerrainAwarePathFinder TerrainAwarePather(this Map map)
        {
            if (!PatherLookup.TryGetValue(map, out TerrainAwarePathFinder pather))
            {
                pather = new TerrainAwarePathFinder(map);
                PatherLookup.Add(map, pather);
            }
            return pather;
        }

        public static void UpdatePawnTerrainChecksAffected(this Map map, IntVec3 c)
        {
            if (PawnReachableLookup.TryGetValue(map, out Dictionary<Pawn, HashSet<int>> mapPawnReachable))
            {
                int mapSizeX = map.Size.x;
                int mapSizeZ = map.Size.z;
                foreach (KeyValuePair<Pawn, HashSet<int>> entry in mapPawnReachable)
                {
                    Pawn pawn = entry.Key;
                    HashSet<int> tileReachableLookup = entry.Value;
                    for (int xmod = -1; xmod < 2; xmod++)
                    {
                        for (int zmod = -1; zmod < 2; zmod++)
                        {
                            IntVec3 check = new IntVec3(c.x + xmod, c.y, c.z + zmod);
                            int checkIndex = map.cellIndices.CellToIndex(check);
                            if (!(check.x < 0 || check.x >= mapSizeX || check.z < 0 || check.z >= mapSizeZ) && tileReachableLookup.Contains(checkIndex))
                            {
                                // Remove the index as we're about to apply the builder against it
                                tileReachableLookup.Remove(checkIndex);
                                map.BuildPawnTerrainCheck(pawn, c, ref tileReachableLookup);
                            }
                        }
                    }
                }
            }
        }
        public static HashSet<int> BuildPawnTerrainCheck(this Map map, Pawn pawn)
        {
            HashSet<int> tileReachableLookup = new HashSet<int>();
            map.BuildPawnTerrainCheck(pawn, pawn.Position, ref tileReachableLookup);
            return tileReachableLookup;
        }

        public static void BuildPawnTerrainCheck(this Map map, Pawn pawn, IntVec3 position, ref HashSet<int> tileReachableLookup)
        {
            HashSet<int> visited = new HashSet<int>();

            int mapSizeX = map.Size.x;
            int mapSizeZ = map.Size.z;
            Queue<IntVec3> frontier = new Queue<IntVec3>();
            frontier.Enqueue(position);
            while (frontier.Count > 0)
            {
                IntVec3 current = frontier.Dequeue();
                int currentIndex = map.cellIndices.CellToIndex(current);
                if (visited.Contains(currentIndex) || tileReachableLookup.Contains(currentIndex))
                {
                    continue;
                }
                bool currentUnreachable = pawn.kindDef.UnreachableTerrainCheck(current.GetTerrain(map));
                visited.Add(currentIndex);
                if (!currentUnreachable)
                {
                    tileReachableLookup.Add(currentIndex);
                    for (int xmod = -1; xmod <= 2; xmod++)
                    {
                        for (int zmod = -1; zmod <= 2; zmod++)
                        {
                            IntVec3 check = new IntVec3(current.x + xmod, current.y, current.z + zmod);
                            int checkIndex = map.cellIndices.CellToIndex(check);
                            if (!(check.x < 0 || check.x >= mapSizeX || check.z < 0 || check.z >= mapSizeZ || visited.Contains(checkIndex)))
                            {
                                frontier.Enqueue(check);
                            }
                        }
                    }
                }
            }
        }

        public static bool FullTerrainCanReach(this Map map, LocalTargetInfo target, Pawn pawn)
        {
            if (pawn != null && pawn.kindDef.HasTerrainChecks())
            {
                if (!PawnReachableLookup.TryGetValue(map, out Dictionary<Pawn, HashSet<int>> mapPawnReachable))
                {
                    mapPawnReachable = new Dictionary<Pawn, HashSet<int>>();
                    PawnReachableLookup[map] = mapPawnReachable;
                }
                // Rebuild if it's missing or if the pawn wasn't in a legal tile and had no map
                if (!mapPawnReachable.TryGetValue(pawn, out HashSet<int> tileReachableLookup) || tileReachableLookup.Count == 0)
                {
                    tileReachableLookup = BuildPawnTerrainCheck(map, pawn);
                    mapPawnReachable[pawn] = tileReachableLookup;
                }
                return tileReachableLookup.Contains(map.cellIndices.CellToIndex(target.Cell));
            }
            return true;
        }


        public static Dictionary<TerrainDef, int> TerrainEdgeCounts(this Map map)
        {
            // Use cache of terrain when possible
            if (!EdgeTerrainLookup.TryGetValue(map, out Dictionary<TerrainDef, int> terrains))
            {
                terrains = new Dictionary<TerrainDef, int>();
                foreach (int x in Enumerable.Range(0, map.Size.x - 1))
                {
                    foreach (int z in Enumerable.Range(0, map.Size.z - 1))
                    {
                        TerrainDef terrain = map.terrainGrid.topGrid[map.cellIndices.CellToIndex(x, z)];
                        if (!terrains.TryGetValue(terrain, out int counter))
                        {
                            counter = 0;
                        }
                        terrains[terrain] = counter + 1;
                    }
                }
                EdgeTerrainLookup.Add(map, terrains);
            }
            return terrains;
        }

        public static bool ThingCanEnter(this Map map, ThingDef kind)
        {
            int reachableTiles = 0;
            foreach (KeyValuePair<TerrainDef, int> entry in map.TerrainEdgeCounts())
            {
                if (!PawnKindDefExtensions.UnreachableTerrainCheck(kind.modExtensions, entry.Key))
                {
                    reachableTiles += entry.Value;
                }
            }
            // If 1/10 of the map edge is reachable
            return reachableTiles > (map.Size.x + map.Size.z) / 5;
        }

        public static bool PawnKindCanEnter(this Map map, PawnKindDef kind)
        {
            return map.ThingCanEnter(kind.race);
        }
    }
}
