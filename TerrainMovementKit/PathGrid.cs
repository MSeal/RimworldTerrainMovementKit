using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using HarmonyLib;
using System.Reflection;

namespace TerrainMovement
{

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
}
