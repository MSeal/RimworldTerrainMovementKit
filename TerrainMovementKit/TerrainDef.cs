using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

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
            if (terrain.modExtensions != null)
            {
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
}
