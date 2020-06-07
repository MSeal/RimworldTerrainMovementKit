using System;
using Verse;

namespace TerrainMovement
{
    public class TerrainMovementPawnRestrictions : DefModExtension
    {
        // Used to indicate what terrain types a pawn must stay on or off of
        public String stayOffTerrainTag = null;
        public String stayOnTerrainTag = null;
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
}
