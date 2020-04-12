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

    [HarmonyPatch(typeof(SignalAction_Ambush), "DoAction", new Type[] { typeof(SignalArgs) })]
    public class SignalAction_Ambush_DoAction_Patch
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
                GenSpawn.Spawn(item, result, map);
                if (!__instance.spawnPawnsOnEdge)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        MoteMaker.ThrowAirPuffUp(item.DrawPos, map);
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

    [HarmonyPatch(typeof(IncidentWorker_FarmAnimalsWanderIn), "CanFireNowSub", new Type[] { typeof(IncidentParms) })]
    public class FarmAnimalsWanderIn_CanFireNowSub_TerrainAware
    {
        public static MethodInfo BaseCanFireNowSubInfo = AccessTools.Method(typeof(IncidentWorker), "CanFireNowSub");
        public static MethodInfo TryFindRandomPawnKindInfo = AccessTools.Method(typeof(IncidentWorker_FarmAnimalsWanderIn), "TryFindRandomPawnKind");

        static bool Prefix(ref bool __result, IncidentWorker_FarmAnimalsWanderIn __instance, IncidentParms parms)
        {
            if (!(bool)BaseCanFireNowSubInfo.InvokeNotOverride(__instance, new object[] { parms }))
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

    [HarmonyPatch(typeof(IncidentWorker_HerdMigration), "CanFireNowSub", new Type[] { typeof(IncidentParms) })]
    public class HerdMigration_CanFireNowSub_TerrainAware
    {
        public static MethodInfo TryFindAnimalKindInfo = AccessTools.Method(typeof(IncidentWorker_HerdMigration), "TryFindAnimalKind");

        static bool Prefix(ref bool __result, ref IncidentWorker_HerdMigration __instance, IncidentParms parms)
        {
            Map map = (Map)parms.target;
            IntVec3 start;
            IntVec3 end;
            object[] parameters = new object[] { map.Tile, null };
            bool flag = (bool)TryFindAnimalKindInfo.Invoke(__instance, parameters);
            PawnKindDef kind = (PawnKindDef)parameters[1];
            __result = flag && __instance.TryFindStartAndEndCells(map, kind, out start, out end);
            return false;
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_HerdMigration), "TryExecuteWorker", new Type[] { typeof(IncidentParms) })]
    public class HerdMigration_TryExecuteWorker_TerrainAware
    {
        public static MethodInfo SendStandardLetterInfo = AccessTools.Method(typeof(IncidentWorker_HerdMigration), "SendStandardLetter", new Type[] { typeof(TaggedString), typeof(TaggedString), typeof(LetterDef), typeof(IncidentParms), typeof(LookTargets), typeof(NamedArgument[]) });
        public static MethodInfo TryFindAnimalKindInfo = AccessTools.Method(typeof(IncidentWorker_HerdMigration), "TryFindAnimalKind");
        public static MethodInfo GenerateAnimalsInfo = AccessTools.Method(typeof(IncidentWorker_HerdMigration), "GenerateAnimals");

        static bool Prefix(ref bool __result, ref IncidentWorker_HerdMigration __instance, IncidentParms parms)
        {
            Map map = (Map)parms.target;
            object[] parameters = new object[] { map.Tile, null };
            bool flag = (bool)TryFindAnimalKindInfo.Invoke(__instance, parameters);
            PawnKindDef animalKind = (PawnKindDef)parameters[1];
            if (!flag)
            {
                __result = false;
                return false;
            }
            if (!__instance.TryFindStartAndEndCells(map, animalKind, out IntVec3 start, out IntVec3 end))
            {
                __result = false;
                return false;
            }
            Rot4 rot = Rot4.FromAngleFlat((map.Center - start).AngleFlat);
            List<Pawn> list = (List<Pawn>)GenerateAnimalsInfo.Invoke(__instance, new object[] { animalKind, map.Tile });
            for (int i = 0; i < list.Count; i++)
            {
                Pawn newThing = list[i];
                IntVec3 loc = CellFinderExtended.RandomClosewalkCellNear(start, map, animalKind, 10);
                GenSpawn.Spawn(newThing, loc, map, rot);
            }
            LordMaker.MakeNewLord(null, new LordJob_ExitMapNear(end, LocomotionUrgency.Walk), map, list);
            TaggedString str = new TaggedString(string.Format(__instance.def.letterText, animalKind.GetLabelPlural()).CapitalizeFirst());
            TaggedString str2 = new TaggedString(string.Format(__instance.def.letterLabel, animalKind.GetLabelPlural().CapitalizeFirst()));
            SendStandardLetterInfo.Invoke(__instance, new object[] { str2, str, __instance.def.letterDef, parms, new LookTargets(list[0]), new NamedArgument[0] { } });
            __result = true;
            return false;
        }
    }

    // TODO IncidentWorker_ManhunterPack.TryExecuteWorker
    // TODO IncidentWorker_CaravanMeeting.TryExecuteWorker
    // TODO IncidentWorker_NeutralGroup.SpawnPawns, .TryResolveParmsGeneral

    // Patching these two methods saves a LOT of other patches, even though this has nothing to do with temperature
    [HarmonyPatch(typeof(TileTemperaturesComp), "SeasonAcceptableFor", new Type[] { typeof(int), typeof(ThingDef) })]
    public class TileTemperaturesComp_SeasonAcceptableFor_TerrainAwareHack
    {
        static void Postfix(ref bool __result, int tile, ThingDef animalRace)
        {
            if (__result && typeof(Pawn).IsAssignableFrom(animalRace.thingClass)) {
                Map map = Current.Game.FindMap(tile);
                if (map != null)
                {
                    __result = map.ThingCanEnter(animalRace);
                }
            }
        }
    }

    [HarmonyPatch(typeof(TileTemperaturesComp), "OutdoorTemperatureAcceptableFor", new Type[] { typeof(int), typeof(ThingDef) })]
    public class TileTemperaturesComp_OutdoorTemperatureAcceptableFor_TerrainAwareHack
    {
        static void Postfix(ref bool __result, int tile, ThingDef animalRace)
        {
            if (__result && typeof(Pawn).IsAssignableFrom(animalRace.thingClass))
            {
                Map map = Current.Game.FindMap(tile);
                if (map != null)
                {
                    __result = map.ThingCanEnter(animalRace);
                }
            }
        }
    }

    //TODO: SiteGenStepUtility replacements
    //TODO: RCellFinder.TryFindRandomSpotJustOutsideColony, .TryFindRandomPawnEntryCell
    //TODO: JobGiver_PrepareCaravan_GatherDownedPawns.FindRandomDropCell
    //TODO: CaravanEnterMapUtility.GetEnterCell
    //TODO: MultipleCaravansCellFinder.FindStartingCellsFor2Groups
    //TODO: JobDriver_FollowClose.MakeNewToils -> RandomClosewalkCellNear

    // Pawns mods
    //TODO: PawnsArrivalModeWorker_EdgeWalkIn.Arrive, .TryResolveRaidSpawnCenter
    //TODO: PawnsArrivalModeWorker_EdgeWalkInGroups.Arrive, .TryResolveRaidSpawnCenter
    //TODO: Toils_LayDown.LayDown -> RandomClosewalkCellNear (Postfix)
}
