using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using HarmonyLib;
using System.Linq;

namespace TerrainMovement
{
    // These pathces are used by Map primarily
    [HarmonyPatch(typeof(Reachability), "CanReach", new Type[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(PathEndMode), typeof(TraverseParms) })]
    public class CanReachMoveCheck
    {
        static bool Prefix(ref bool __result, Map ___map, IntVec3 start, LocalTargetInfo dest, PathEndMode peMode, TraverseParms traverseParams)
        {
            if (___map.UnreachableTerrainCheck(dest, traverseParams.pawn))
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
            if (map.UnreachableTerrainCheck(target, pawn))
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

    public static class MapExtensions
    {
        public static Dictionary<Map, TerrainAwarePathFinder> PatherLookup = new Dictionary<Map, TerrainAwarePathFinder>();
        public static Dictionary<int, Map> TileLookup = new Dictionary<int, Map>();
        public static Dictionary<Map, Dictionary<TerrainDef, int>> EdgeTerrainLookup = new Dictionary<Map, Dictionary<TerrainDef, int>>();
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

        public static bool UnreachableTerrainCheck(this Map map, LocalTargetInfo target, Pawn pawn)
        {
            return pawn == null ? false : pawn.kindDef.UnreachableTerrainCheck(target.Cell.GetTerrain(map));
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
