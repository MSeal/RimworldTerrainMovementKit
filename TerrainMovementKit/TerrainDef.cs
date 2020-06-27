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

        public static IEnumerable<(StatDef moveStat, StatDef costStat)> TerrainMovementStatDefs(this TerrainDef terrain, bool defaultMovementAllowed = true, LocomotionUrgency? urgency = null)
        {
            HashSet<StatDef> disallowedPathCostsStats = terrain.TerrainMovementDisallowedPathCostStat();
            // Check for if default movement is allowed or not
            if (defaultMovementAllowed && !disallowedPathCostsStats.Contains(StatDefOf.MoveSpeed))
            {
                yield return (StatDefOf.MoveSpeed, StatDefOf.MoveSpeed);
            }
            if (terrain.modExtensions != null)
            {
                foreach (DefModExtension ext in terrain.modExtensions)
                {
                    TerrainMovementStatDef moveStatDef = terrain.LoadTerrainMovementStatDefExtension(ext);
                    if (moveStatDef != null)
                    {
                        if (urgency is LocomotionUrgency realUrgency)
                        {
                            if (moveStatDef.UrgencyDisallowed(realUrgency))
                            {
                                continue;
                            }
                        }
                        StatDef newCostStat;
                        // There's no actual StatDef for pathCost, so map it to null
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
                            yield return (newMoveStat, newCostStat);
                        }
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
            return (int)Math.Round(terrain.GetStatValueAbstract(costStat), 0);
        }
    }
}
