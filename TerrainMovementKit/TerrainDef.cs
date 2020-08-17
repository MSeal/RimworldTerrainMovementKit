using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace TerrainMovement
{
    public static class TerrainDefExtensions
    {
        public static List<TerrainMovementStatDef> AllTerrainMovementStats = null;

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
                        // There's no actual StatDef for pathCost, so map it to MoveSpeed
                        if (restrictions.disallowedPathCostStat == null || restrictions.disallowedPathCostStat == "pathCost")
                        {
                            restrictionDef = StatDefOf.MoveSpeed;
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

        public static (StatDef moveStat, StatDef costStat) TerrainMovementStatDefPair(this TerrainDef terrain, TerrainMovementStatDef moveStatDef, bool defaultMovementAllowed = true, LocomotionUrgency urgency = LocomotionUrgency.None)
        {
            HashSet<StatDef> disallowedPathCostsStats = terrain.TerrainMovementDisallowedPathCostStat();
            if (moveStatDef != null && moveStatDef.UrgencyAllowed(urgency))
            {
                StatDef newCostStat;
                // If there's no actual StatDef for pathCost, so map it to null
                if (moveStatDef.pawnSpeedStat == null || moveStatDef.pawnSpeedStat == "pathCost")
                {
                    if (defaultMovementAllowed)
                    {
                        newCostStat = StatDefOf.MoveSpeed;
                    }
                    else
                    {
                        newCostStat = null;
                    }
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
                        if (defaultMovementAllowed)
                        {
                            newMoveStat = StatDefOf.MoveSpeed;
                        }
                        else
                        {
                            newMoveStat = null;
                        }
                    }
                    else
                    {
                        newMoveStat = StatDef.Named(moveStatDef.pawnSpeedStat);
                    }
                    return (newMoveStat, newCostStat);
                }
            }
            return (null, null);
        }

        public static IEnumerable<(StatDef moveStat, StatDef costStat)> AnyTerrainMovementStatDefs(this TerrainDef terrain, bool defaultMovementAllowed = true, LocomotionUrgency urgency = LocomotionUrgency.None)
        {
            HashSet<StatDef> disallowedPathCostsStats = terrain.TerrainMovementDisallowedPathCostStat();
            // Check for if default movement is allowed or not
            if (defaultMovementAllowed && !disallowedPathCostsStats.Contains(StatDefOf.MoveSpeed))
            {
                yield return (StatDefOf.MoveSpeed, StatDefOf.MoveSpeed);
            }

            // Memoize statdef terrain modifications
            if (AllTerrainMovementStats == null)
            {
                AllTerrainMovementStats = new List<TerrainMovementStatDef>();
                foreach (StatDef stat in DefDatabase<StatDef>.AllDefs)
                {
                    TerrainMovementStatDef statExt = stat.GetModExtension<TerrainMovementStatDef>();
                    if (statExt != null) {
                        AllTerrainMovementStats.Add(statExt);
                    }
                }
            }

            foreach (TerrainMovementStatDef moveStatDef in AllTerrainMovementStats)
            {
                yield return terrain.TerrainMovementStatDefPair(moveStatDef, defaultMovementAllowed, urgency);
            }
        }

        public static IEnumerable<(StatDef moveStat, StatDef costStat)> TerrainMovementStatDefs(this TerrainDef terrain, bool defaultMovementAllowed = true, LocomotionUrgency urgency = LocomotionUrgency.None)
        {
            foreach (var pair in terrain.AnyTerrainMovementStatDefs(defaultMovementAllowed, urgency))
            {
                yield return pair;
            }
            if (terrain.modExtensions != null)
            {
                foreach (DefModExtension ext in terrain.modExtensions)
                {
                    TerrainMovementStatDef moveStatDef = terrain.LoadTerrainMovementStatDefExtension(ext);
                    var pair = terrain.TerrainMovementStatDefPair(moveStatDef, defaultMovementAllowed, urgency);
                    if (!(pair.moveStat == null && pair.costStat == null))
                    {
                        yield return pair;
                    }
                }
            }
        }

        public static int MovementCost(this TerrainDef terrain, StatDef costStat)
        {
            if (costStat == null)
            {
                return 99999;
            }
            if (costStat == StatDefOf.MoveSpeed)
            {
                return terrain.pathCost;
            }
            int cost = (int)Math.Round(terrain.GetStatValueAbstract(costStat), 0);
            return cost <= 0 ? 99999 : cost;
        }
    }
}
