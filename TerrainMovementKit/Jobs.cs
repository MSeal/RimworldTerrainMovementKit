using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using HarmonyLib;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace TerrainMovement
{
    [HarmonyPatch(typeof(JobGiver_PrepareCaravan_GatherDownedPawns), "FindRandomDropCell")]
    public class JobGiver_PrepareCaravan_GatherDownedPawns_FindRandomDropCell_TerrainAware_Patch
    {
        public static bool Prefix(ref IntVec3 __result, Pawn pawn, Pawn downedPawn)
        {
            __result = CellFinderExtended.RandomClosewalkCellNear(pawn.mindState.duty.focusSecond.Cell, pawn.Map, pawn.kindDef, 6, (IntVec3 x) => x.Standable(pawn.Map) && StoreUtility.IsGoodStoreCell(x, pawn.Map, downedPawn, pawn, pawn.Faction));
            return false;
        }
    }


    [HarmonyPatch]
    public static class JobDriver_FollowClose_MakeNewToils_Patch
    {
        public static MethodInfo RandomClosewalkCellNearInfo = AccessTools.Method(typeof(CellFinderExtended), "RandomClosewalkCellNear");

        static MethodBase TargetMethod()
        {
            // Should find the inner delegated method
            return typeof(JobDriver_FollowClose).MethodMatching(methods =>
            {
                return methods.Where(m => m.Name.StartsWith("<MakeNewToils>b"))
                    .OrderBy(m => m.Name)
                    .LastOrDefault();
            });
        }

        // Changes
        // CellFinder.RandomClosewalkCellNear(IntVec3 root, Map map, PawnKindDef kind, int radius, Predicate<IntVec3> extraValidator = null) 
        // ->
        // CellFinderExtended.RandomClosewalkCellNear(IntVec3 root, Map map, PawnKindDef kind, int radius, Predicate<IntVec3> extraValidator = null)
        [HarmonyPriority(Priority.First)]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return instructions.ReplaceFunctionArgument(
                RandomClosewalkCellNearInfo,
                new List<CodeInstruction>() {
                    // Load pawn, then get pawn.kindDef
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldfld, typeof(JobDriver).Field("pawn")),
                    new CodeInstruction(OpCodes.Ldfld, typeof(Pawn).Field("kindDef"))
                },
                3,
                "RandomClosewalkCellNear",
                "JobDriver_FollowClose.MakeNewToils");
        }
    }

    [HarmonyPatch(typeof(JobDriver_Wait), "DecorateWaitToil")]
    public class JobDriver_Wait_DecorateWaitToil_TerrainAware_Patch
    {
        public static bool Prefix(ref JobDriver_Wait __instance, Toil wait)
        {
            // Default to Amble since it instead defaults to Job
            // This causes disallowedLocomotionUrgencies to be difficult to use otherwise for non-action pawns
            __instance.job.locomotionUrgency = LocomotionUrgency.Amble;
            return true;
        }
    }

    [HarmonyPatch(typeof(ReservationManager), "CanReserve")]
    public class ReservationManager_Reservation_CanReserve_ReachCheckPatch
    {
        public static void Postfix(ref bool __result, Pawn claimant, LocalTargetInfo target)
        {
            // This COULD mess up reservations that don't care about reachability .. but there's a lot of methods
            // that seem to assume you can reach things in how they are coded and some are in delegates that can't
            // be easily patched. Now that terrain restrictions are more prominent we're using the loosest check
            // we can do that still enforces some measure of reachability
            if (__result && !claimant.CanReach(target, PathEndMode.ClosestTouch, Danger.Deadly))
            {
                __result = false;
            }
        }
    }
}
