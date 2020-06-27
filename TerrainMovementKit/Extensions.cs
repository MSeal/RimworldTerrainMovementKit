﻿using System;
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
    }
    public class TerrainMovementTerrainRestrictions : DefModExtension
    {
        public String disallowedPathCostStat = "pathCost";
    }
}
