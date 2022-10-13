using RimWorld;
using Verse;
using UnityEngine;
using HarmonyLib;
using System;
using System.Linq;
using System.Collections.Generic;
using Verse.AI.Group;
using System.Reflection;
using RimWorld.Planet;
using Verse.AI;
using System.Reflection.Emit;

namespace TerrainMovement
{
    [HarmonyPatch(typeof(ManhunterPackIncidentUtility), "TryFindManhunterAnimalKind", new Type[] { typeof(float), typeof(int), typeof(PawnKindDef) }, new ArgumentType[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out })]
    public class ManhunterPackIncidentUtility_AnimalKind_Patch
    {
        static bool Prefix(ref bool __result, float points, int tile, out PawnKindDef animalKind)
        {
            MapExtensions.TileLookup.TryGetValue(tile, out Map map);
            bool flag = DefDatabase<PawnKindDef>.AllDefs.Where(
                (PawnKindDef k) =>
                    k.RaceProps.Animal && k.canArriveManhunter &&
                    (tile == -1 || Find.World.tileTemperatures.SeasonAndOutdoorTemperatureAcceptableFor(tile, k.race)) &&
                    (map == null || map.PawnKindCanEnter(k))

            ).TryRandomElementByWeight(
                (PawnKindDef k) => ManhunterPackIncidentUtility.ManhunterAnimalWeight(k, points), out animalKind);
            if (!flag)
            {
                List<PawnKindDef> tmpAnimalKinds = new List<PawnKindDef>();
                tmpAnimalKinds.AddRange(DefDatabase<PawnKindDef>.AllDefs.Where((PawnKindDef k) => k.RaceProps.Animal && k.canArriveManhunter && map.PawnKindCanEnter(k)));
                tmpAnimalKinds.Sort((PawnKindDef a, PawnKindDef b) => b.combatPower.CompareTo(a.combatPower));
                animalKind = tmpAnimalKinds.Take(Math.Max(2, Mathf.FloorToInt(0.15f * (float)tmpAnimalKinds.Count))).RandomElement();
                __result = animalKind != null;
            }
            else
            {
                __result = flag;
            }
            return false;
        }
    }

    // TODO: Make this a transpile change (lots of work...)
    [HarmonyPatch(typeof(SignalAction_Ambush), "DoAction", new Type[] { typeof(SignalArgs) })]
    public class SignalAction_Ambush_DoAction_Patch2
    {
        static MethodInfo GenerateAmbushPawnsInfo = AccessTools.Method(typeof(SignalAction_Ambush), "GenerateAmbushPawns");
        static bool Prefix(ref SignalAction_Ambush __instance, SignalArgs args)
        {
            Map map = __instance.Map;
            if (__instance.points <= 0f)
            {
                return false;
            }
            List<Pawn> list = new List<Pawn>();
            foreach (Pawn item in (IEnumerable<Pawn>)GenerateAmbushPawnsInfo.Invoke(__instance, new object[] { }))
            {
                // Skip out if the pawn isn't suitable for the map
                if (!map.PawnKindCanEnter(item.kindDef))
                {
                    Find.WorldPawns.PassToWorld(item);
                    break;
                }

                IntVec3 result;
                if (__instance.spawnPawnsOnEdge)
                {
                    // Changed to CellFinderExtended
                    if (!CellFinderExtended.TryFindRandomEdgeCellWith((IntVec3 x) => x.Standable(map) && !x.Fogged(map) && map.reachability.CanReachColony(x), map, item.kindDef, CellFinder.EdgeRoadChance_Ignore, out result))
                    {
                        Find.WorldPawns.PassToWorld(item);
                        break;
                    }
                }
                // TODO: PawnKind aware find cell around
                else if (!SiteGenStepUtility.TryFindSpawnCellAroundOrNear(__instance.spawnAround, __instance.spawnNear, map, out result))
                {
                    Find.WorldPawns.PassToWorld(item);
                    break;
                }
                if (__instance.useDropPods)
                {
                    DropPodUtility.DropThingsNear(result, map, Gen.YieldSingle(item));
                }
                else
                {
                    GenSpawn.Spawn(item, result, map);
                    if (!__instance.spawnPawnsOnEdge)
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            FleckMaker.ThrowAirPuffUp(item.DrawPos, map);
                        }
                    }
                }
                list.Add(item);
            }
            if (!list.Any())
            {
                return false;
            }
            if (__instance.ambushType == SignalActionAmbushType.Manhunters)
            {
                for (int j = 0; j < list.Count; j++)
                {
                    list[j].health.AddHediff(HediffDefOf.Scaria);
                    list[j].mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.ManhunterPermanent);
                }
            }
            else
            {
                Faction faction = list[0].Faction;
                LordMaker.MakeNewLord(faction, new LordJob_AssaultColony(faction), map, list);
            }
            if (!__instance.spawnPawnsOnEdge)
            {
                for (int k = 0; k < list.Count; k++)
                {
                    list[k].jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 120));
                    list[k].Rotation = Rot4.Random;
                }
            }
            Find.LetterStack.ReceiveLetter("LetterLabelAmbushInExistingMap".Translate(), "LetterAmbushInExistingMap".Translate(Faction.OfPlayer.def.pawnsPlural).CapitalizeFirst(), LetterDefOf.ThreatBig, list[0]);
            return false;
        }
    }

    // Need to replace the whole function here to rearrange things rather than transpile it
    [HarmonyPatch(typeof(IncidentWorker_FarmAnimalsWanderIn), "CanFireNowSub", new Type[] { typeof(IncidentParms) })]
    public class FarmAnimalsWanderIn_CanFireNowSub_TerrainAware
    {
        public static MethodInfo BaseCanFireNowSubInfo = AccessTools.Method(typeof(IncidentWorker), "CanFireNowSub").CreateNonVirtualDynamicMethod();
        public static MethodInfo TryFindRandomPawnKindInfo = AccessTools.Method(typeof(IncidentWorker_FarmAnimalsWanderIn), "TryFindRandomPawnKind");

        static bool Prefix(ref bool __result, IncidentWorker_FarmAnimalsWanderIn __instance, IncidentParms parms)
        {
            if (!(bool)BaseCanFireNowSubInfo.Invoke(null, new object[] { __instance, parms }))
            {
                __result = false;
            }
            else
            {
                Map map = (Map)parms.target;
                object[] parameters = new object[] { map, null };
                // Check for kind first, because we need it for the new TryFindRandomPawnEntryCell
                bool flag = (bool)TryFindRandomPawnKindInfo.Invoke(__instance, parameters);
                PawnKindDef kind = (PawnKindDef)parameters[1];
                __result = flag && RCellFinderExtended.TryFindRandomPawnEntryCell(out IntVec3 _, map, kind, CellFinder.EdgeRoadChance_Animal);
            }
            
            return false;
        }
    }

    // Need to replace the whole function here to rearrange things rather than transpile it
    [HarmonyPatch(typeof(IncidentWorker_FarmAnimalsWanderIn), "TryExecuteWorker", new Type[] { typeof(IncidentParms) })]
    public class FarmAnimalsWanderIn_TryExecuteWorker_TerrainAware
    {
        public static MethodInfo SendStandardLetterInfo = AccessTools.Method(typeof(IncidentWorker_FarmAnimalsWanderIn), "SendStandardLetter", new Type[] { typeof(TaggedString), typeof(TaggedString), typeof(LetterDef), typeof(IncidentParms), typeof(LookTargets), typeof(NamedArgument[]) });
        public static MethodInfo TryFindRandomPawnKindInfo = AccessTools.Method(typeof(IncidentWorker_FarmAnimalsWanderIn), "TryFindRandomPawnKind");

        static bool Prefix(ref bool __result, IncidentWorker_FarmAnimalsWanderIn __instance, IncidentParms parms)
        {
            Map map = (Map)parms.target;
            object[] parameters = new object[] { map, null };
            bool flag = (bool)TryFindRandomPawnKindInfo.Invoke(__instance, parameters);
            if (!flag)
            {
                return false;
            }
            PawnKindDef kind = (PawnKindDef)parameters[1];
            // Check this AFTER getting a pawn kind
            if (!RCellFinderExtended.TryFindRandomPawnEntryCell(out IntVec3 result, map, kind, CellFinder.EdgeRoadChance_Animal))
            {
                return false;
            }
            int num = Mathf.Clamp(GenMath.RoundRandom(2.5f / kind.RaceProps.baseBodySize), 2, 10);
            for (int i = 0; i < num; i++)
            {
                IntVec3 loc = CellFinderExtended.RandomClosewalkCellNear(result, map, kind, 12);
                Pawn pawn = PawnGenerator.GeneratePawn(kind);
                GenSpawn.Spawn(pawn, loc, map, Rot4.Random);
                pawn.SetFaction(Faction.OfPlayer);
            }
            SendStandardLetterInfo.Invoke(__instance, new object[] { "LetterLabelFarmAnimalsWanderIn".Translate(kind.GetLabelPlural()).CapitalizeFirst(), "LetterFarmAnimalsWanderIn".Translate(kind.GetLabelPlural()), LetterDefOf.PositiveEvent, parms, new LookTargets(result, map), new NamedArgument[0] { } });
            return false;
        }
    }

    public static class IncidentWorker_HerdMigration_Extensions
    {
        public static bool TryFindStartAndEndCells(this IncidentWorker_HerdMigration hm, Map map, PawnKindDef kind, out IntVec3 start, out IntVec3 end)
        {
            if (!RCellFinderExtended.TryFindRandomPawnEntryCell(out start, map, kind, CellFinder.EdgeRoadChance_Animal))
            {
                end = IntVec3.Invalid;
                return false;
            }
            end = IntVec3.Invalid;
            for (int i = 0; i < 8; i++)
            {
                IntVec3 startLocal = start;
                if (!CellFinderExtended.TryFindRandomEdgeCellWith((IntVec3 x) => map.reachability.CanReach(startLocal, x, PathEndMode.OnCell, TraverseMode.NoPassClosedDoors, Danger.Deadly), map, kind, CellFinder.EdgeRoadChance_Ignore, out IntVec3 result))
                {
                    break;
                }
                if (!end.IsValid || result.DistanceToSquared(start) > end.DistanceToSquared(start))
                {
                    end = result;
                }
            }
            return end.IsValid;
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_HerdMigration), "CanFireNowSub")]
    public static class IncidentWorker_HerdMigration_CanFireNowSub_Patch
    {
        public static MethodInfo TryFindStartAndEndCellsInfo = AccessTools.Method(typeof(IncidentWorker_HerdMigration_Extensions), "TryFindStartAndEndCells");

        // Changes
        // TryFindStartAndEndCells(Map map, out IntVec3 start, out IntVec3 end) 
        // ->
        // TryFindStartAndEndCells(Map map, PawnKindDef kind, out IntVec3 start, out IntVec3 end)
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return instructions.ReplaceFunctionArgument(
                TryFindStartAndEndCellsInfo,
                new CodeInstruction(OpCodes.Ldloc_1),
                2,
                "TryFindStartAndEndCells",
                "IncidentWorker_HerdMigration.CanFireNowSub");
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_HerdMigration), "TryExecuteWorker")]
    public class HerdMigration_TryExecuteWorker_TerrainAware_Patch
    {
        public static MethodInfo TryFindStartAndEndCellsInfo = AccessTools.Method(typeof(IncidentWorker_HerdMigration_Extensions), "TryFindStartAndEndCells");
        public static MethodInfo RandomClosewalkCellNearInfo = AccessTools.Method(typeof(CellFinderExtended), "RandomClosewalkCellNear");

        // Changes
        // CellFinder.RandomClosewalkCellNear(IntVec3 root, Map map, int radius, Predicate<IntVec3> extraValidator = null) 
        // ->
        // CellFinderExtended.RandomClosewalkCellNear(IntVec3 root, Map map, PawnKindDef kind, int radius, Predicate<IntVec3> extraValidator = null)
        //
        // Changes
        // TryFindStartAndEndCells(Map map, out IntVec3 start, out IntVec3 end) 
        // ->
        // TryFindStartAndEndCells(Map map, PawnKindDef kind, out IntVec3 start, out IntVec3 end)
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return instructions.ReplaceFunctionArgument(
                TryFindStartAndEndCellsInfo,
                new CodeInstruction(OpCodes.Ldloc_1),
                2,
                "TryFindStartAndEndCells",
                "IncidentWorker_HerdMigration.TryExecuteWorker"
            ).ReplaceFunctionArgument(
                RandomClosewalkCellNearInfo,
                new CodeInstruction(OpCodes.Ldloc_1),
                2,
                "RandomClosewalkCellNear",
                "IncidentWorker_HerdMigration.TryExecuteWorker");
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_ManhunterPack), "TryExecuteWorker")]
    public static class IncidentWorker_ManhunterPack_TryExecuteWorker_Patch
    {
        public static MethodInfo TryFindRandomPawnEntryCellInfo = AccessTools.Method(typeof(RCellFinderExtended), "TryFindRandomPawnEntryCell");
        public static MethodInfo RandomClosewalkCellNearInfo = AccessTools.Method(typeof(CellFinderExtended), "RandomClosewalkCellNear");

        // Changes
        // RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 result, Map map, float roadChance, bool allowFogged) 
        // ->
        // RCellFinderExtended.TryFindRandomPawnEntryCell(out IntVec3 result, Map map, PawnKindDef kind, float roadChance, bool allowFogged)
        //
        // Changes
        // CellFinder.RandomClosewalkCellNear(IntVec3 root, Map map, PawnKindDef kind, int radius, Predicate<IntVec3> extraValidator = null) 
        // ->
        // CellFinderExtended.RandomClosewalkCellNear(IntVec3 root, Map map, PawnKindDef kind, int radius, Predicate<IntVec3> extraValidator = null)
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return instructions.ReplaceFunctionArgument(
                TryFindRandomPawnEntryCellInfo,
                new CodeInstruction(OpCodes.Ldloc_1),
                3,
                "TryFindRandomPawnEntryCell",
                "IncidentWorker_ManhunterPack.TryExecuteWorker"
            ).ReplaceFunctionArgument(
                RandomClosewalkCellNearInfo,
                new CodeInstruction(OpCodes.Ldloc_1),
                2,
                "RandomClosewalkCellNear",
                "IncidentWorker_ManhunterPack.TryExecuteWorker");
        }
    }

    public static class PawnGroupMakerParmsExtended
    {
        public static bool MapAllowed(this PawnGroupMakerParms parms, ref PawnGenOptionWithXenotype pawnOpt)
        {
            if (parms.tile >= 0 && MapExtensions.TileLookup.TryGetValue(parms.tile, out Map map))
            {
                return map.PawnKindCanEnter(pawnOpt.Option.kind);
            }
            return true;
        }
    }

    // This makes caravans and other group spawners restrict pawns to ones that can enter the map
    [HarmonyPatch(typeof(PawnGroupMakerUtility), "ChoosePawnGenOptionsByPoints")]
    public static class PawnGroupMakerUtility_ChoosePawnGenOptionsByPoints_Patch
    {
        public static MethodInfo TryFindRandomPawnEntryCellInfo = AccessTools.Method(typeof(PawnGroupMakerParmsExtended), "MapAllowed");

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            bool costInjectFound = false;
            int injectIndex = -1;
            int costCounts = 0;
            CodeInstruction pawnOptOp = null;
            object endLoopLoc = null;
            // We need to inject a conditional check after the second get_Cost call
            for (int i = 0; i < list.Count(); i++)
            {
                var inst = list[i];
                if (injectIndex < 0 && inst.opcode == OpCodes.Call && (inst.operand as MethodInfo)?.Name == "get_Cost")
                {
                    costCounts += 1;
                    if (costCounts == 1)
                    {
                        injectIndex = i + 3;
                        pawnOptOp = list[i - 1];
                        endLoopLoc = list[i + 2].operand;
                    }
                }
                if (i == injectIndex)
                {
                    // Add groupParms
                    yield return new CodeInstruction(OpCodes.Ldarg_2);
                    // Add pawnOpt
                    yield return pawnOptOp;
                    // Call MapAllowed
                    yield return new CodeInstruction(OpCodes.Call, TryFindRandomPawnEntryCellInfo);
                    // Exit conditional if false
                    yield return new CodeInstruction(OpCodes.Brfalse_S, endLoopLoc);
                    costInjectFound = true;
                }
                yield return inst;
            }
            
            if (!costInjectFound)
            {
                Log.ErrorOnce(String.Format("[TerrainMovementKit] Cannot find location to inject pawn terrain awareness into {0}, skipping patch", "PawnGroupMakerUtility.ChoosePawnGenOptionsByPoints"), "PawnGroupMakerUtility.ChoosePawnGenOptionsByPoints".GetHashCode());
            }
        }
    }

    // This makes caravan placement spawn on valid tiles to avoid teleportation
    [HarmonyPatch(typeof(CaravanEnterMapUtility), "Enter", new Type[] {
        typeof(Caravan), typeof(Map), typeof(CaravanEnterMode), typeof(CaravanDropInventoryMode), typeof(bool), typeof(Predicate<IntVec3>)
    })]
    public static class CaravanEnterMapUtility_Enter_Patch
    {
        public static MethodInfo GetEnterCellInfo = AccessTools.Method(typeof(CaravanEnterMapUtility), "GetEnterCell");
        public static MethodInfo EnterWithFuncInfo = AccessTools.Method(typeof(CaravanEnterMapUtility), "Enter", new Type[] {
            typeof(Caravan), typeof(Map), typeof(Func<Pawn, IntVec3>), typeof(CaravanDropInventoryMode), typeof(bool)
        });

        public static bool Prefix(Caravan caravan, Map map, CaravanEnterMode enterMode, CaravanDropInventoryMode dropInventoryMode = CaravanDropInventoryMode.DoNotDrop, bool draftColonists = false, Predicate<IntVec3> extraCellValidator = null)
        {
            if (enterMode == CaravanEnterMode.None)
            {
                Log.Error("Caravan " + caravan + " tried to enter map " + map + " with enter mode " + enterMode);
                enterMode = CaravanEnterMode.Edge;
            }
            Predicate<IntVec3> wrapped = (IntVec3 x) => (extraCellValidator == null || extraCellValidator(x)) && map.PawnKindCanEnter(caravan.pawns.InnerListForReading.First().kindDef);
            IntVec3 enterCell = (IntVec3)GetEnterCellInfo.Invoke(null, new object[] { caravan, map, enterMode, wrapped });
            Func<Pawn, IntVec3> spawnCellGetter = (Pawn p) => CellFinderExtended.RandomSpawnCellForPawnNear(enterCell, map, p.kindDef);
            EnterWithFuncInfo.Invoke(null, new object[] { caravan, map, spawnCellGetter, dropInventoryMode, draftColonists });
            return false;
        }
    }

    // This makes caravans not targeting a map to use player home defaults for restrictions
    [HarmonyPatch(typeof(IncidentWorker_CaravanMeeting), "GenerateCaravanPawns")]
    public static class IncidentWorker_CaravanMeeting_GenerateCaravanPawns_Patch
    {
        public static MethodInfo GetEnterCellInfo = AccessTools.Method(typeof(CaravanEnterMapUtility), "GetEnterCell");
        public static MethodInfo EnterWithFuncInfo = AccessTools.Method(typeof(CaravanEnterMapUtility), "Enter", new Type[] {
            typeof(Caravan), typeof(Map), typeof(Func<Pawn, IntVec3>), typeof(CaravanDropInventoryMode), typeof(bool)
        });

        public static bool Prefix(ref List<Pawn> __result, Faction faction)
        {
            __result = PawnGroupMakerUtility.GeneratePawns(new PawnGroupMakerParms
            {
                tile = Find.AnyPlayerHomeMap.Tile,
                groupKind = PawnGroupKindDefOf.Trader,
                faction = faction,
                points = TraderCaravanUtility.GenerateGuardPoints(),
                dontUseSingleUseRocketLaunchers = true
            }).ToList();
            return false;
        }
    }

    // TODO transpiler for better compatability
    [HarmonyPatch(typeof(IncidentWorker_NeutralGroup), "SpawnPawns", new Type[] { typeof(IncidentParms) })]
    public class IncidentWorker_NeutralGroup_SpawnPawns_TerrainAware_Patch
    {
        public static MethodInfo PawnGroupKindDefInfo = AccessTools.PropertyGetter(typeof(IncidentWorker_NeutralGroup), "PawnGroupKindDef");

        public static bool Prefix(ref List<Pawn> __result, IncidentWorker_NeutralGroup __instance, IncidentParms parms)
        {
            Map map = (Map)parms.target;
            List<Pawn> list = PawnGroupMakerUtility.GeneratePawns(IncidentParmsUtility.GetDefaultPawnGroupMakerParms((PawnGroupKindDef)PawnGroupKindDefInfo.Invoke(__instance, new object[] { }), parms, ensureCanGenerateAtLeastOnePawn: true), warnOnZeroResults: false).ToList();
            foreach (Pawn item in list)
            {
                IntVec3 loc = CellFinderExtended.RandomClosewalkCellNear(parms.spawnCenter, map, item.kindDef, 5);
                GenSpawn.Spawn(item, loc, map);
            }
            __result = list;
            return false;
        }
    }
}
