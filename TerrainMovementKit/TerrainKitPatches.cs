using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;
using HarmonyLib;
using System.Reflection;


namespace TerrainMovement
{
    [StaticConstructorOnStartup]
    public static class KitLoader
    {
        public const String HarmonyId = "net.mseal.rimworld.mod.terrain.movement";

        static KitLoader()
        {
            if (!Harmony.HasAnyPatches(HarmonyId))
            {
                var harmony = new Harmony(HarmonyId);
                harmony.PatchAll(Assembly.GetExecutingAssembly());
            }
        }
    }

    public class TerrainMovementStatDef : DefModExtension
    {
        public String terrainPathCostStat = null;
        public String pawnSpeedStat = null;
    }
    public class TerrainMovementPawnRestrictions : DefModExtension
    {
        // Used to indicate what terrain types a pawn must stay on or off of
        public String stayOffTerrainTag = null;
        public String stayOnTerrainTag = null;
    }

    static class MapExtensions
    {
        public static Dictionary<int, TerrainAwarePathFinder> PatherLookup = new Dictionary<int, TerrainAwarePathFinder>();

        public static void ResetLookup()
        {
            PatherLookup = new Dictionary<int, TerrainAwarePathFinder>();
        }

        public static TerrainAwarePathFinder TerrainAwarePather(this Map map)
        {
            if (!PatherLookup.TryGetValue(map.uniqueID, out TerrainAwarePathFinder pather))
            {
                pather = new TerrainAwarePathFinder(map);
                PatherLookup.Add(map.uniqueID, pather);
            }
            return pather;
        }

        public static bool UnreachableTerrainCheck(this Map map, LocalTargetInfo target, Pawn pawn)
        {
            if (pawn != null)
            {
                TerrainDef terrain = target.Cell.GetTerrain(map);
                foreach (DefModExtension ext in pawn.def.modExtensions)
                {
                    if (ext is TerrainMovementPawnRestrictions)
                    {
                        TerrainMovementPawnRestrictions restrictions = ext as TerrainMovementPawnRestrictions;
                        if (restrictions.stayOffTerrainTag != null && terrain.HasTag(restrictions.stayOffTerrainTag))
                        {
                            return true;
                        }
                        if (restrictions.stayOnTerrainTag != null && !terrain.HasTag(restrictions.stayOnTerrainTag))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(Map), "FinalizeInit", new Type[0])]
    internal static class Map_FinalizeInit_Patch
    {
        private static void Postfix()
        {
            MapExtensions.ResetLookup();
        }
    }

    [HarmonyPatch(typeof(PathFinder), "FindPath", new Type[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(TraverseParms), typeof(PathEndMode) })]
    class SwimmerPathPatch
    {
        static bool Prefix(ref PawnPath __result, Map ___map, IntVec3 start, LocalTargetInfo dest, TraverseParms traverseParms, PathEndMode peMode)
        {
            __result = ___map.TerrainAwarePather().FindPath(start, dest, traverseParms, peMode);
            return false;
        }
    }

    [HarmonyPatch(typeof(Reachability), "CanReach", new Type[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(PathEndMode), typeof(TraverseParms) })]
    class CanReachMoveCheck
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
    class CanReachImmediateMoveCheck
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

    [HarmonyPatch(typeof(PathGrid), "CalculatedCostAt", new Type[] { typeof(IntVec3), typeof(bool), typeof(IntVec3) })]
    class TerrainAwareCalculatedCostAt
    {
        static bool Prefix(ref int __result, Map ___map, IntVec3 c, bool perceivedStatic, IntVec3 prevCell)
        {
            int num = 0;
            bool flag = false;
            TerrainDef terrainDef = ___map.terrainGrid.TerrainAt(c);
            
            // TODO finish this...
            /*StatDef moveStat = pawn.TerrainSpeedStat(terrainDef);
            StatDef terrainPathCostStat = terrainDef.TerrainPathCostStat();
            int terrainPathCost = (terrainPathCostStat != null) ? (int)terrainDef.GetStatValueAbstract(terrainPathCostStat) : 0;
            float terrainSpeed = pawn.GetStatValue(moveStat, true);*/

            if (terrainDef == null || terrainDef.passability == Traversability.Impassable)
            {
               __result = 10000;
                return false;
            }
            num = terrainDef.pathCost;
            List<Thing> list = ___map.thingGrid.ThingsListAt(c);
            for (int i = 0; i < list.Count; i++)
            {
                Thing thing = list[i];
                if (thing.def.passability == Traversability.Impassable)
                {
                    __result = 10000;
                    return false;
                }
                if (!IsPathCostIgnoreRepeater(thing.def) || !prevCell.IsValid || !ContainsPathCostIgnoreRepeater(prevCell))
                {
                    int pathCost = thing.def.pathCost;
                    if (pathCost > num)
                    {
                        num = pathCost;
                    }
                }
                if (thing is Building_Door && prevCell.IsValid)
                {
                    Building edifice = prevCell.GetEdifice(___map);
                    if (edifice != null && edifice is Building_Door)
                    {
                        flag = true;
                    }
                }
            }
            int num2 = SnowUtility.MovementTicksAddOn(___map.snowGrid.GetCategory(c));
            if (num2 > num)
            {
                num = num2;
            }
            if (flag)
            {
                num += 45;
            }
            if (perceivedStatic)
            {
                for (int j = 0; j < 9; j++)
                {
                    IntVec3 b = GenAdj.AdjacentCellsAndInside[j];
                    IntVec3 c2 = c + b;
                    if (!c2.InBounds(___map))
                    {
                        continue;
                    }
                    Fire fire = null;
                    list = ___map.thingGrid.ThingsListAtFast(c2);
                    for (int k = 0; k < list.Count; k++)
                    {
                        fire = (list[k] as Fire);
                        if (fire != null)
                        {
                            break;
                        }
                    }
                    if (fire != null && fire.parent == null)
                    {
                        num = ((b.x != 0 || b.z != 0) ? (num + 150) : (num + 1000));
                    }
                }
            }
            __result = num;
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), "CostToMoveIntoCell", new Type[] { typeof(IntVec3) })]
    class TerrainAwareFollowerPatch
    {
        public static int CostToMoveIntoCell(Pawn pawn, IntVec3 c)
        {
            int num;
            if (c.x == pawn.Position.x || c.z == pawn.Position.z)
            {
                num = pawn.TerrainAwareTicksPerMoveCardinal(c);
            }
            else
            {
                num = pawn.TerrainAwareTicksPerMoveDiagonal(c);
            }
            int gridCost = pawn.Map.pathGrid.CalculatedCostAt(c, false, pawn.Position);
            // TODO INSTEAD USE TerrainAwareCalculatedCostAt changes
            TerrainDef terrain = c.GetTerrain(pawn.Map);
            StatDef moveStat = pawn.TerrainSpeedStat(terrain);
            StatDef terrainPathCostStat = terrain.TerrainPathCostStat();
            int terrainPathCost = (terrainPathCostStat != null) ? (int)terrain.GetStatValueAbstract(terrainPathCostStat) : 0;
            float terrainSpeed = pawn.GetStatValue(moveStat, true);
            if (terrainSpeed > 0)
            {
                if (terrainPathCost > 0)
                {
                    // Replace grid cost with terrain cost
                    gridCost += terrainPathCost - terrain.pathCost;
                }
                else
                {
                    // TODO FIGURE OUT WHAT TO DO HERE
                    // Reduce the path penalty for swimming by 10x
                    gridCost -= (terrain.pathCost * 9) / 10;
                }
            }
            num += gridCost;
            Building edifice = c.GetEdifice(pawn.Map);
            if (edifice != null)
            {
                num += (int)edifice.PathWalkCostFor(pawn);
            }
            if (num > 450)
            {
                num = 450;
            }
            if (pawn.CurJob != null)
            {
                Pawn locomotionUrgencySameAs = pawn.jobs.curDriver.locomotionUrgencySameAs;
                if (locomotionUrgencySameAs != null && locomotionUrgencySameAs != pawn && locomotionUrgencySameAs.Spawned)
                {
                    int num2 = TerrainAwareFollowerPatch.CostToMoveIntoCell(locomotionUrgencySameAs, c);
                    if (num < num2)
                    {
                        num = num2;
                    }
                }
                else
                {
                    switch (pawn.jobs.curJob.locomotionUrgency)
                    {
                        case LocomotionUrgency.Amble:
                            num *= 3;
                            if (num < 60)
                            {
                                num = 60;
                            }
                            break;
                        case LocomotionUrgency.Walk:
                            num *= 2;
                            if (num < 50)
                            {
                                num = 50;
                            }
                            break;
                        case LocomotionUrgency.Jog:
                            break;
                        case LocomotionUrgency.Sprint:
                            num = Mathf.RoundToInt((float)num * 0.75f);
                            break;
                    }
                }
            }
            return Mathf.Max(num, 1);
        }

        static bool Prefix(ref int __result, Pawn ___pawn, IntVec3 c)
        {
            __result = CostToMoveIntoCell(___pawn, c);
            return false;
        }
    }

    static class TerrainDefExtensions
    {
        public static StatDef TerrainPathCostStat(this TerrainDef terrain)
        {
            StatDef costStat = null;
            foreach (DefModExtension ext in terrain.modExtensions)
            {
                if (ext is TerrainMovementStatDef)
                {
                    TerrainMovementStatDef moveStatDef = ext as TerrainMovementStatDef;
                    if (moveStatDef.terrainPathCostStat == null)
                    {
                        Log.ErrorOnce(
                            String.Format("Terrain movement extension for '{0}' is missing 'terrainPathCostStat'",
                            terrain.defName), terrain.GetHashCode() + 10);
                    }
                    StatDef pathStat = StatDef.Named(moveStatDef.terrainPathCostStat);
                    if (costStat != null)
                    {
                        Log.ErrorOnce(
                            String.Format("Found duplicate movement extension for '{0}'. Applying last seen extension for pathCostStat", terrain.defName),
                            terrain.GetHashCode() + 1);
                    }
                    costStat = pathStat;
                }
            }
            return costStat;
        }
    }

    static class PawnExtensions
    {
        public static StatDef TerrainSpeedStat(this Pawn pawn, TerrainDef terrain)
        {
            StatDef moveStat = null;
            foreach (DefModExtension ext in terrain.modExtensions)
            {
                if (ext is TerrainMovementStatDef)
                {
                    TerrainMovementStatDef moveStatDef = ext as TerrainMovementStatDef;
                    if (moveStatDef.pawnSpeedStat == null)
                    {
                        Log.ErrorOnce(
                            String.Format("Terrain movement extension for '{0}' is missing 'pawnSpeedStat'", terrain.defName),
                            terrain.GetHashCode());
                        continue;
                    }
                    StatDef terrainStat = StatDef.Named(moveStatDef.pawnSpeedStat);
                    if (moveStat != null)
                    {
                        Log.ErrorOnce(
                            String.Format("Found duplicate movement extension for '{0}'. Applying faster speed stat", terrain.defName),
                            terrain.GetHashCode() + 1);
                        if (pawn.GetStatValue(moveStat) > pawn.GetStatValue(terrainStat))
                        {
                            terrainStat = moveStat;
                        }
                    }
                    moveStat = terrainStat;
                }
            }

            return moveStat;
        }

        public static float TerrainAwareSpeed(this Pawn pawn, TerrainDef terrain)
        {
            return pawn.GetStatValue(pawn.TerrainSpeedStat(terrain), true);
        }

        public static int TerrainAwareTicksPerMoveCardinal(this Pawn pawn, IntVec3 loc)
        {
            return pawn.TerrainAwareTicksPerMove(pawn.Map.terrainGrid.TerrainAt(loc), false);
        }

        public static int TerrainAwareTicksPerMoveDiagonal(this Pawn pawn, IntVec3 loc)
        {
            return pawn.TerrainAwareTicksPerMove(pawn.Map.terrainGrid.TerrainAt(loc), true);
        }

        public static int TerrainAwareTicksPerMoveCardinal(this Pawn pawn, TerrainDef terrain)
        {
            return pawn.TerrainAwareTicksPerMove(terrain, false);
        }

        public static int TerrainAwareTicksPerMoveDiagonal(this Pawn pawn, TerrainDef terrain)
        {
            return pawn.TerrainAwareTicksPerMove(terrain, true);
        }

        public static int TerrainAwareTicksPerMove(this Pawn pawn, TerrainDef terrain, bool diagonal)
        {
            float num = TerrainAwareSpeed(pawn, terrain);
            if (RestraintsUtility.InRestraints(pawn))
            {
                num *= 0.35f;
            }
            if (pawn.carryTracker != null && pawn.carryTracker.CarriedThing != null && pawn.carryTracker.CarriedThing.def.category == ThingCategory.Pawn)
            {
                num *= 0.6f;
            }
            float num2 = num / 60f;
            float num3;
            if (num2 == 0f)
            {
                num3 = 450f;
            }
            else
            {
                num3 = 1f / num2;
                // This is actually a bug in the base game -- this applies to all path calculations if you started under a roof
                // Not sure how to fix without incurring 
                if (pawn.Spawned && !pawn.Map.roofGrid.Roofed(pawn.Position))
                {
                    num3 /= pawn.Map.weatherManager.CurMoveSpeedMultiplier;
                }
                if (diagonal)
                {
                    num3 *= 1.41421f;
                }
            }
            int value = Mathf.RoundToInt(num3);
            return Mathf.Clamp(value, 1, 450);
        }
    }
}
