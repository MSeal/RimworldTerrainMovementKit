using System;
using RimWorld;
using Verse;
using Verse.AI;
using HarmonyLib;
using Verse.AI.Group;
using System.Linq;
using UnityEngine;

namespace TerrainMovement
{
    static class CellFinderExtended
    {
        public static bool TryFindRandomPawnExitCell(Pawn searcher, out IntVec3 result)
        {
            return TryFindRandomEdgeCellWith((IntVec3 c) => !searcher.Map.roofGrid.Roofed(c) && c.Walkable(searcher.Map) && searcher.CanReach(c, PathEndMode.OnCell, Danger.Some), searcher.Map, searcher.kindDef, 0f, out result);
        }

        public static bool TryFindRandomEdgeCellWith(Predicate<IntVec3> validator, Map map, PawnKindDef kind, float roadChance, out IntVec3 result)
        {
            Predicate<IntVec3> wrapped = (IntVec3 x) => validator(x) && map.PawnKindCanEnter(kind);
            return CellFinder.TryFindRandomEdgeCellWith(wrapped, map, roadChance, out result);
        }

        public static IntVec3 RandomSpawnCellForPawnNear(IntVec3 root, Map map, PawnKindDef kind, int firstTryWithRadius = 4)
        {
            if (TryFindRandomSpawnCellForPawnNear(root, map, kind, out IntVec3 result, firstTryWithRadius))
            {
                return result;
            }
            return root;
        }

        public static IntVec3 RandomClosewalkCellNear(IntVec3 root, Map map, PawnKindDef kind, int radius, Predicate<IntVec3> extraValidator = null)
        {
            if (TryRandomClosewalkCellNear(root, map, kind, radius, out IntVec3 result, extraValidator))
            {
                return result;
            }
            return root;
        }

        public static bool TryRandomClosewalkCellNear(IntVec3 root, Map map, PawnKindDef kind, int radius, out IntVec3 result, Predicate<IntVec3> extraValidator = null)
        {
            return TryFindRandomReachableCellNear(root, map, kind, radius, TraverseParms.For(TraverseMode.NoPassClosedDoors), (IntVec3 c) => c.Standable(map) && (extraValidator == null || extraValidator(c)), null, out result);
        }

        public static IntVec3 RandomClosewalkCellNearNotForbidden(IntVec3 root, Map map, PawnKindDef kind, int radius, Pawn pawn)
        {
            if (!TryFindRandomReachableCellNear(root, map, kind, radius, TraverseParms.For(TraverseMode.NoPassClosedDoors), (IntVec3 c) => !c.IsForbidden(pawn) && c.Standable(map), null, out IntVec3 result))
            {
                return RandomClosewalkCellNear(root, map, kind, radius);
            }
            return result;
        }

        public static bool TryFindRandomReachableCellNear(IntVec3 root, Map map, PawnKindDef kind, float radius, TraverseParms traverseParms, Predicate<IntVec3> cellValidator, Predicate<Region> regionValidator, out IntVec3 result, int maxRegions = 999999)
        {
            Predicate<IntVec3> wrapped = (IntVec3 x) => cellValidator(x) && map.PawnKindCanEnter(kind);
            return CellFinder.TryFindRandomReachableCellNear(root, map, radius, traverseParms, wrapped, regionValidator, out result, maxRegions);
        }

        public static bool TryFindRandomSpawnCellForPawnNear(IntVec3 root, Map map, PawnKindDef kind, out IntVec3 result, int firstTryWithRadius = 4)
        {
            if (root.Standable(map) && root.GetFirstPawn(map) == null)
            {
                result = root;
                return true;
            }
            bool rootFogged = root.Fogged(map);
            int num = firstTryWithRadius;
            for (int i = 0; i < 3; i++)
            {
                if (TryFindRandomReachableCellNear(root, map, kind, num, TraverseParms.For(TraverseMode.NoPassClosedDoors), (IntVec3 c) => c.Standable(map) && (rootFogged || !c.Fogged(map)) && c.GetFirstPawn(map) == null, null, out result))
                {
                    return true;
                }
                num *= 2;
            }
            num = firstTryWithRadius + 1;
            while (true)
            {
                if (TryRandomClosewalkCellNear(root, map, kind, num, out result))
                {
                    return true;
                }
                if (num > map.Size.x / 2 && num > map.Size.z / 2)
                {
                    break;
                }
                num *= 2;
            }
            result = root;
            return false;
        }
    }

    static class RCellFinderExtended
    {
        public static bool TryFindRandomPawnEntryCell(out IntVec3 result, Map map, PawnKindDef kind, float roadChance, bool allowFogged = false, Predicate<IntVec3> extraValidator = null)
        {
            Predicate<IntVec3> wrapped = (IntVec3 x) => (extraValidator == null || extraValidator(x)) && map.PawnKindCanEnter(kind);
            return RCellFinder.TryFindRandomPawnEntryCell(out result, map, roadChance, allowFogged, wrapped);
        }
    }

    [HarmonyPatch(typeof(Region), "Allows")]
    public class TraverseParms_Allows_Movement_Restrictions
    {
        static void Postfix(ref bool __result, ref Region __instance, TraverseParms tp, bool isDestination)
        {
            if (__result && tp.pawn != null && __instance.Map != null && !__instance.Map.PawnKindCanEnter(tp.pawn.kindDef))
            {
                 __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(TransitionAction_EnsureHaveExitDestination), "DoAction", new Type[] { typeof(Transition) })]
    public class EnsureHaveExitDestinationKindCheck
    {
        static bool Prefix(Transition trans)
        {
            LordToil_Travel lordToil_Travel = (LordToil_Travel)trans.target;
            if (!lordToil_Travel.HasDestination())
            {
                Pawn pawn = lordToil_Travel.lord.ownedPawns.RandomElement();
                if (!CellFinderExtended.TryFindRandomPawnExitCell(pawn, out IntVec3 result))
                {
                    RCellFinderExtended.TryFindRandomPawnEntryCell(out result, pawn.Map, pawn.kindDef, 0f);
                }
                lordToil_Travel.SetDestination(result);
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(WildAnimalSpawner), "SpawnRandomWildAnimalAt", new Type[] { typeof(IntVec3) })]
    public class SpawnRandomWildAnimalAtKindCheck
    {
        static bool Prefix(ref bool __result, IntVec3 loc, Map ___map)
        {
            PawnKindDef pawnKindDef = ___map.Biome.AllWildAnimals.Where(
                (PawnKindDef a) => ___map.mapTemperature.SeasonAcceptableFor(a.race) && ___map.PawnKindCanEnter(a) && !a.UnreachableLocationCheck(___map, loc)
            ).RandomElementByWeight((PawnKindDef def) => ___map.Biome.CommonalityOfAnimal(def) / def.wildGroupSize.Average);
            if (pawnKindDef == null)
            {
                Log.Error("No spawnable animals right now.");
                __result = false;
                return false;
            }
            int randomInRange = pawnKindDef.wildGroupSize.RandomInRange;
            int radius = Mathf.CeilToInt(Mathf.Sqrt(pawnKindDef.wildGroupSize.max));
            for (int i = 0; i < randomInRange; i++)
            {
                IntVec3 loc2 = CellFinderExtended.RandomClosewalkCellNear(loc, ___map, pawnKindDef, radius);
                GenSpawn.Spawn(PawnGenerator.GeneratePawn(pawnKindDef), loc2, ___map);
            }
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(RCellFinder), "RandomAnimalSpawnCell_MapGen")]
    public static class RCellFinder_RandomAnimalSpawnCell_MapGen_IgnoreWander_Patch
    {
        // The delegated method makes transpiling this a PITA... so we're just going to overwrite it and ignore avoidWander.
        static bool Prefix(ref IntVec3 __result, Map map)
        {
            int numStand = 0;
            int numRoom = 0;
            int numTouch = 0;
            if (!CellFinderLoose.TryGetRandomCellWith(delegate (IntVec3 c)
            {
                if (!c.Standable(map))
                {
                    numStand++;
                    return false;
                }
                /*if (c.GetTerrain(map).avoidWander)
                {
                    return false;
                }*/
                Room room = c.GetRoom(map);
                if (room == null)
                {
                    numRoom++;
                    return false;
                }
                if (!room.TouchesMapEdge)
                {
                    numTouch++;
                    return false;
                }
                return true;
            }, map, 1000, out IntVec3 result))
            {
                result = CellFinder.RandomCell(map);
                Log.Warning("RandomAnimalSpawnCell_MapGen failed: numStand=" + numStand + ", numRoom=" + numRoom + ", numTouch=" + numTouch + ". PlayerStartSpot=" + MapGenerator.PlayerStartSpot + ". Returning " + result);
            }
            __result = result;
            return false;
        }
    }
}
