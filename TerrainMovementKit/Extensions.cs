using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace TerrainMovement
{
    public class TerrainMovementPawnRestrictions : DefModExtension
    {
        // Used to indicate what terrain types a pawn must stay on or off of
        public String stayOffTerrainTag = null;
        public String stayOnTerrainTag = null;
        public bool defaultMovementAllowed = true;
    }
    public class TerrainMovementPawnKindGraphics : DefModExtension
    {
        // Used to indicate custom terrain for a given movement type
        public String pawnSpeedStat; // Required
        public GraphicData bodyGraphicData;
        public GraphicData femaleGraphicData = null;

        public bool StatAffectedGraphic(StatDef moveStat)
        {
            return pawnSpeedStat.Trim().ToLower() == moveStat.defName.ToLower();
        }
    }

    public class TerrainMovementStatDef : DefModExtension
    {
        public String terrainPathCostStat = "pathCost";
        public String pawnSpeedStat = "MoveSpeed";
        public List<String> disallowedLocomotionUrgencies = new List<String>();

        public bool UrgencyDisallowed(LocomotionUrgency urgency)
        {
            String urgencyName = urgency.ToString("G").ToLower();
            foreach (String disallowed in disallowedLocomotionUrgencies)
            {
                if (urgencyName == disallowed.ToLower().Trim())
                {
                    return true;
                }
            }
            return false;
        }

        public bool UrgencyAllowed(LocomotionUrgency urgency)
        {
            return !UrgencyDisallowed(urgency);
        }
    }
    public class TerrainMovementTerrainRestrictions : DefModExtension
    {
        public String disallowedPathCostStat = "pathCost";
    }
}
