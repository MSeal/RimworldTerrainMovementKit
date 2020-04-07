using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;
using HarmonyLib;
using System.Reflection;
using System.Linq;
using System.IO;

namespace TerrainMovement
{
    public sealed class HarmonyStarter : Mod
    {
        public const String HarmonyId = "net.mseal.rimworld.mod.terrain.movement";

        public HarmonyStarter(ModContentPack content) : base(content)
        {
            Assembly terrainAssembly = Assembly.GetExecutingAssembly();
            string DLLName = terrainAssembly.GetName().Name;
            Version loadedVersion = terrainAssembly.GetName().Version;
            Version laterVersion = loadedVersion;
                
            List<ModContentPack> runningModsListForReading = LoadedModManager.RunningModsListForReading;
            foreach (ModContentPack mod in runningModsListForReading)
            {
                foreach (FileInfo item in from f in ModContentPack.GetAllFilesForMod(mod, "Assemblies/", (string e) => e.ToLower() == ".dll") select f.Value)
                {
                    var newAssemblyName = AssemblyName.GetAssemblyName(item.FullName);
                    if (newAssemblyName.Name == DLLName && newAssemblyName.Version > laterVersion)
                    {
                        laterVersion = newAssemblyName.Version;
                        Log.Error(String.Format("TerrainMovementKit load order error detected. {0} is loading an older version {1} before {2} loads version {3}. Please put the TerrainMovementKit, or BiomesCore modes above this one if they are active.",
                            content.Name, loadedVersion, mod.Name, laterVersion));
                    }
                }
            }

            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll(terrainAssembly);
        }
    }

    public class TerrainMovementStatDef : DefModExtension
    {
        public String terrainPathCostStat = "pathCost";
        public String pawnSpeedStat = "MoveSpeed";
    }
    public class TerrainMovementTerrainRestrictions : DefModExtension
    {
        public String disallowedPathCostStat = "pathCost";
    }
    public class TerrainMovementPawnRestrictions : DefModExtension
    {
        // Used to indicate what terrain types a pawn must stay on or off of
        public String stayOffTerrainTag = null;
        public String stayOnTerrainTag = null;
    }

    public static class MapExtensions
    {
        public static Dictionary<int, TerrainAwarePathFinder> PatherLookup = new Dictionary<int, TerrainAwarePathFinder>();

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
            return pawn == null ? false : pawn.UnreachableTerrainCheck(target.Cell.GetTerrain(map));
        }
    }

    [HarmonyPatch(typeof(PathFinder), "FindPath", new Type[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(TraverseParms), typeof(PathEndMode) })]
    public class TerrainPathPatch
    {
        static bool Prefix(ref PawnPath __result, Map ___map, IntVec3 start, LocalTargetInfo dest, TraverseParms traverseParms, PathEndMode peMode)
        {
            __result = ___map.TerrainAwarePather().FindPath(start, dest, traverseParms, peMode);
            return false;
        }
    }

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

    [HarmonyPatch(typeof(Pawn_PathFollower), "CostToMoveIntoCell", new Type[] { typeof(Pawn), typeof(IntVec3) })]
    public class TerrainAwareFollowerPatch
    {
        static bool Prefix(ref int __result, Pawn pawn, IntVec3 c)
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
            // Replace the calculated cost with one which is pawn / terrain aware
            //num += pawn.Map.pathGrid.CalculatedCostAt(c, perceivedStatic: false, pawn.Position);
            num += pawn.Map.pathGrid.TerrainCalculatedCostAt(pawn.Map, pawn, c, false, pawn.Position);
            // Rest of function is the same...
            Building edifice = c.GetEdifice(pawn.Map);
            if (edifice != null)
            {
                num += edifice.PathWalkCostFor(pawn);
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
                    int num2 = 0;
                    // Call the prefix directly because the method we're patching is private
                    Prefix(ref num2, locomotionUrgencySameAs, c);
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
            __result = Mathf.Max(num, 1);
            return false;
        }
    }

    public static class PathGridExtensions
    {
        public static MethodInfo IsPathCostIgnoreRepeaterInfo = AccessTools.Method(typeof(PathGrid), "IsPathCostIgnoreRepeater");
        public static MethodInfo ContainsPathCostIgnoreRepeaterInfo = AccessTools.Method(typeof(PathGrid), "ContainsPathCostIgnoreRepeater");

        public static int ApplyTerrainModToCalculatedCost(this PathGrid grid, TerrainDef terrain, int calcCost, int terrainMoveCost)
        {
            return calcCost - terrain.pathCost + terrainMoveCost;
        }

        public static int TerrainCalculatedCostAt(this PathGrid grid, Map map, Pawn pawn, IntVec3 c, bool perceivedStatic, IntVec3 prevCell)
        {
            int num;
            bool flag = false;
            TerrainDef terrainDef = map.terrainGrid.TerrainAt(c);
            if (terrainDef == null || terrainDef.passability == Traversability.Impassable)
            {
                return 10000;
            }
            // Replace the pathCost with a terrain aware value based on the best movement option for a pawn
            //num = terrainDef.pathCost;
            num = pawn.TerrainMoveCost(terrainDef);
            List<Thing> list = map.thingGrid.ThingsListAt(c);
            for (int i = 0; i < list.Count; i++)
            {
                Thing thing = list[i];
                if (thing.def.passability == Traversability.Impassable)
                {
                    return 10000;
                }
                if (!(bool)IsPathCostIgnoreRepeaterInfo.Invoke(null, new object[] { thing.def }) || !prevCell.IsValid || !(bool)ContainsPathCostIgnoreRepeaterInfo.Invoke(grid, new object[] { prevCell }))
                {
                    int pathCost = thing.def.pathCost;
                    if (pathCost > num)
                    {
                        num = pathCost;
                    }
                }
                if (thing is Building_Door && prevCell.IsValid)
                {
                    Building edifice = prevCell.GetEdifice(map);
                    if (edifice != null && edifice is Building_Door)
                    {
                        flag = true;
                    }
                }
            }
            int num2 = SnowUtility.MovementTicksAddOn(map.snowGrid.GetCategory(c));
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
                    if (!c2.InBounds(map))
                    {
                        continue;
                    }
                    Fire fire = null;
                    list = map.thingGrid.ThingsListAtFast(c2);
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
            return num;
        }
    }

    public static class TerrainDefExtensions
    {
        // Provides an opportunity for other mods to manipulate terrain movement stats based on their mod extensions
        public static TerrainMovementTerrainRestrictions LoadTerrainMovementTerrainRestrictionsExtension(this TerrainDef terrain, DefModExtension ext)
        {
            if (ext is TerrainMovementTerrainRestrictions)
            {
                return ext as TerrainMovementTerrainRestrictions;
            }
            return null;
        }
        public static HashSet<StatDef> TerrainMovementDisallowedPathCostStat(this TerrainDef terrain)
        {
            HashSet<StatDef> disallowedPathCostStat = new HashSet<StatDef>();
            if (terrain.modExtensions != null)
            {
                foreach (DefModExtension ext in terrain.modExtensions)
                {
                    TerrainMovementTerrainRestrictions restrictions = terrain.LoadTerrainMovementTerrainRestrictionsExtension(ext);
                    if (restrictions != null)
                    {
                        StatDef restrictionDef;
                        // There's no actual StatDef for pathCost, so map it to null
                        if (restrictions.disallowedPathCostStat == null || restrictions.disallowedPathCostStat == "pathCost")
                        {
                            restrictionDef = null;
                        }
                        else
                        {
                            restrictionDef = StatDef.Named(restrictions.disallowedPathCostStat);
                        }
                        disallowedPathCostStat.Add(restrictionDef);
                    }
                }
            }
            return disallowedPathCostStat;
        }

        // Provides an opportunity for other mods to manipulate terrain movement stats based on their mod extensions
        public static TerrainMovementStatDef LoadTerrainMovementStatDefExtension(this TerrainDef terrain, DefModExtension ext)
        {
            if (ext is TerrainMovementStatDef)
            {
                return ext as TerrainMovementStatDef;
            }
            return null;
        }
        
        public static IEnumerable<(StatDef moveStat, StatDef costStat)> TerrainMovementStatDefs(this TerrainDef terrain)
        {
            HashSet<StatDef> disallowedPathCostsStats = terrain.TerrainMovementDisallowedPathCostStat();
            // Check for if default movement is allowed or not
            if (!disallowedPathCostsStats.Contains(null))
            {
                yield return (StatDefOf.MoveSpeed, null);
            }
            if (terrain.modExtensions != null) {
                foreach (DefModExtension ext in terrain.modExtensions)
                {
                    TerrainMovementStatDef moveStatDef = terrain.LoadTerrainMovementStatDefExtension(ext);
                    if (moveStatDef != null)
                    {
                        StatDef newCostStat;
                        // There's no actual StatDef for pathCost, so map it to null
                        if (moveStatDef.pawnSpeedStat == null || moveStatDef.pawnSpeedStat == "pathCost")
                        {
                            newCostStat = null;
                        }
                        else
                        {
                            newCostStat = StatDef.Named(moveStatDef.terrainPathCostStat);
                        }
                        // Opt out if the terrain disallows this costStat
                        if (!disallowedPathCostsStats.Contains(newCostStat))
                        {
                            StatDef newMoveStat;
                            if (moveStatDef.pawnSpeedStat == null)
                            {
                                newMoveStat = StatDefOf.MoveSpeed;
                            }
                            else
                            {
                                newMoveStat = StatDef.Named(moveStatDef.pawnSpeedStat);
                            }
                            yield return (newMoveStat, newCostStat);
                        }
                    }
                }
            }
        }

        public static int MovementCost(this TerrainDef terrain, StatDef maybeCostStat)
        {
            return maybeCostStat == null ? terrain.pathCost : (int)Math.Round(terrain.GetStatValueAbstract(maybeCostStat), 0);
        }
    }

    public static class PawnExtensions
    {
        // Provides an opportunity for other mods to manipulate terrain movement stats based on their mod extensions
        public static TerrainMovementPawnRestrictions LoadTerrainMovementPawnRestrictionsExtension(this Pawn pawn, DefModExtension ext)
        {
            if (ext is TerrainMovementPawnRestrictions)
            {
                return ext as TerrainMovementPawnRestrictions;
            }
            return null;
        }

        public static bool UnreachableTerrainCheck(this Pawn pawn, TerrainDef terrain)
        {
            if (pawn != null && pawn.def.modExtensions != null)
            {
                foreach (DefModExtension ext in pawn.def.modExtensions)
                {
                    TerrainMovementPawnRestrictions restrictions = pawn.LoadTerrainMovementPawnRestrictionsExtension(ext);
                    if (restrictions != null)
                    {
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

        public static (StatDef moveStat, StatDef costStat) BestTerrainMovementStatDefs(this Pawn pawn, TerrainDef terrain)
        {
            (StatDef moveStat, StatDef costStat) bestStats = (null, null);
            float curSpeed = -1;
            foreach (var terrainStats in terrain.TerrainMovementStatDefs())
            {
                // Lazily calculate curSpeed for performance reasons
                if (bestStats.moveStat == null)
                {
                    bestStats = terrainStats;
                }
                else
                {
                    if (curSpeed < 0)
                    {
                        curSpeed = pawn.GetStatValue(bestStats.moveStat) / terrain.MovementCost(bestStats.costStat);
                    }
                    float newSpeed = pawn.GetStatValue(terrainStats.moveStat ?? StatDefOf.MoveSpeed) / terrain.MovementCost(terrainStats.costStat);
                    // Find highest movement statistic for this pawn
                    if (newSpeed >= curSpeed)
                    {
                        curSpeed = newSpeed;
                        bestStats = terrainStats;
                    }
                }
            }

            return bestStats;
        }

        public static StatDef TerrainMoveStat(this Pawn pawn, TerrainDef terrain)
        {
            return pawn.BestTerrainMovementStatDefs(terrain).moveStat;
        }

        public static int TerrainMoveCost(this Pawn pawn, TerrainDef terrain)
        {
            return terrain.MovementCost(pawn.BestTerrainMovementStatDefs(terrain).costStat);
        }

        public static float TerrainSpeed(this Pawn pawn, TerrainDef terrain)
        {
            return pawn.GetStatValue(pawn.TerrainMoveStat(terrain));
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
            float num = TerrainSpeed(pawn, terrain);
            // Rest of this is the same as vanilla function
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
