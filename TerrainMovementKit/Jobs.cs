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

	[HarmonyPatch(typeof(JobDriver_LayDown), "LayDownToil")]
	public class JobDriver_LayDown_LayDownToil_LocmotionAware
	{
		public static bool Prefix(ref JobDriver_LayDown __instance)
		{
			__instance.job.locomotionUrgency = LocomotionUrgency.None;
			return true;
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

	// Achtung! modifies Notify_Teleported to not end the current job, but that also causes Notify_Teleported_Int
	// to never reset set the pather's next position. This reapplys that positional assignemnt without manipulating
	// job status.
	[HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.TryRecoverFromUnwalkablePosition))]
	static class Pawn_PathFollower_TryRecoverFromUnwalkablePosition_Patch
	{
		public static void Postfix(ref bool __result, Pawn ___pawn)
		{
			// Only apply if the pawn was successfully teleported and not destroyed
			if (__result)
            {
				___pawn.pather.Notify_Teleported_Int();
			}
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
            if (__result && claimant.def.HasTerrainChecks() && !claimant.CanReach(target, PathEndMode.ClosestTouch, Danger.Deadly))
            {
                __result = false;
            }
        }
    }

	// Uncomment if you want visibility into job pathing failures
    /*[HarmonyPatch(typeof(Pawn_PathFollower), "PatherFailed")]
    public class Pawn_PathFollower_Failed_Debug
    {
        private static void Postfix(Pawn ___pawn)
        {
            Log.Warning("Job " + ___pawn.jobs.curDriver.ToStringSafe() + " for pawn " + ___pawn.ToStringSafe() + " Failed Pathing");
        }
    }*/

	// Uncomment this if you want to debug RCellFinder CanWander
	/*[HarmonyPatch(typeof(RCellFinder), "CanWanderToCell")]
	public class CanWanderToCell_Debug
	{
		private static bool Prefix(ref bool __result, IntVec3 c, Pawn pawn, IntVec3 root, Func<Pawn, IntVec3, IntVec3, bool> validator, int tryIndex, Danger maxDanger)
		{
			bool flag = true;
			if (!c.WalkableBy(pawn.Map, pawn))
			{
				if (flag)
				{
					Log.Warning("Not Walkable for " + pawn.ToStringSafe() + " @ " + c.ToStringSafe());
					pawn.Map.debugDrawer.FlashCell(c, 0f, "walk");
				}
				__result = false;
				return false;
			}
			if (c.IsForbidden(pawn))
			{
				if (flag)
				{
					Log.Warning("Forbidden for " + pawn.ToStringSafe() + " @ " + c.ToStringSafe());
					pawn.Map.debugDrawer.FlashCell(c, 0.25f, "forbid");
				}
				__result = false;
				return false;
			}
			if (tryIndex < 10 && !c.Standable(pawn.Map))
			{
				if (flag)
				{
					Log.Warning("Not Standable for " + pawn.ToStringSafe() + " @ " + c.ToStringSafe());
					pawn.Map.debugDrawer.FlashCell(c, 0.25f, "stand");
				}
				__result = false;
				return false;
			}
			if (!pawn.CanReach(c, PathEndMode.OnCell, maxDanger))
			{
				if (flag)
				{
					Log.Warning("Can't reach " + pawn.ToStringSafe() + " @ " + c.ToStringSafe());
					pawn.Map.debugDrawer.FlashCell(c, 0.6f, "reach");
				}
				__result = false;
				return false;
			}
			if (PawnUtility.KnownDangerAt(c, pawn.Map, pawn))
			{
				if (flag)
				{
					Log.Warning("Knwon Danger for " + pawn.ToStringSafe() + " @ " + c.ToStringSafe());
					pawn.Map.debugDrawer.FlashCell(c, 0.1f, "trap");
				}
				__result = false;
				return false;
			}
			if (tryIndex < 10)
			{
				if (c.GetTerrain(pawn.Map).avoidWander)
				{
					if (flag)
					{
						Log.Warning("Avoid wander" + pawn.ToStringSafe() + " @ " + c.ToStringSafe());
						pawn.Map.debugDrawer.FlashCell(c, 0.39f, "terr");
					}
					__result = false;
					return false;
				}
				if (pawn.Map.pathing.For(pawn).pathGrid.PerceivedPathCostAt(c) > 20)
				{
					if (flag)
					{
						Log.Warning("PCost too high for " + pawn.ToStringSafe() + " @ " + c.ToStringSafe());
						pawn.Map.debugDrawer.FlashCell(c, 0.4f, "pcost");
					}
					__result = false;
					return false;
				}
				if ((int)c.GetDangerFor(pawn, pawn.Map) > 1)
				{
					if (flag)
					{
						Log.Warning("Danger for " + pawn.ToStringSafe() + " @ " + c.ToStringSafe());
						pawn.Map.debugDrawer.FlashCell(c, 0.4f, "danger");
					}
					__result = false;
					return false;
				}
			}
			else if (tryIndex < 15 && c.GetDangerFor(pawn, pawn.Map) == Danger.Deadly)
			{
				if (flag)
				{
					Log.Warning("Deadly for " + pawn.ToStringSafe() + " @ " + c.ToStringSafe());
					pawn.Map.debugDrawer.FlashCell(c, 0.4f, "deadly");
				}
				__result = false;
				return false;
			}
			if (!pawn.Map.pawnDestinationReservationManager.CanReserve(c, pawn))
			{
				if (flag)
				{
					Log.Warning("Can't reserve for " + pawn.ToStringSafe() + " @ " + c.ToStringSafe());
					pawn.Map.debugDrawer.FlashCell(c, 0.75f, "resvd");
				}
				__result = false;
				return false;
			}
			if (validator != null && !validator(pawn, c, root))
			{
				if (flag)
				{
					Log.Warning("Validator failed for " + pawn.ToStringSafe() + " @ " + c.ToStringSafe());
					pawn.Map.debugDrawer.FlashCell(c, 0.15f, "valid");
				}
				__result = false;
				return false;
			}
			if (c.GetDoor(pawn.Map) != null)
			{
				if (flag)
				{
					Log.Warning("Door blocking " + pawn.ToStringSafe() + " @ " + c.ToStringSafe());
					pawn.Map.debugDrawer.FlashCell(c, 0.32f, "door");
				}
				__result = false;
				return false;
			}
			if (c.ContainsStaticFire(pawn.Map))
			{
				if (flag)
				{
					Log.Warning("Fire blocking " + pawn.ToStringSafe() + " @ " + c.ToStringSafe());
					pawn.Map.debugDrawer.FlashCell(c, 0.9f, "fire");
				}
				__result = false;
				return false;
			}
			__result = true;
			return false;
		}
	}*/
}
