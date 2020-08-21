using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using HarmonyLib;
using UnityEngine;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection;

namespace TerrainMovement
{
    public static class PawnGraphicSetExtension
    {

        public static int CalculateGraphicsHash(this PawnGraphicSet graphicsSet, TerrainMovementPawnKindGraphics graphicsExt, Rot4 facing, RotDrawMode bodyCondition)
        {
            int num = facing.AsInt + 1000 * (int)bodyCondition;
            if (graphicsExt != null)
            {
                num += graphicsExt.pawnSpeedStat.GetHashCode();
            }
            return num;
        }
    }

    [HarmonyPatch(typeof(PawnGraphicSet), "AllResolved", MethodType.Getter)]
    public class TerrainAwarePawnGraphicAllResolved
    {
        static bool Prefix(Pawn ___pawn, ref bool __result, ref PawnGraphicSet __instance, ref int ___cachedMatsBodyBaseHash)
        {
            Pawn pawn = ___pawn;
            StatDef moveStat = pawn.BestTerrainMoveStat();
            if (moveStat == null || moveStat == StatDefOf.MoveSpeed)
            {
                return true;
            }
            TerrainMovementPawnKindGraphics graphicsExt = pawn.LoadTerrainMovementPawnKindGraphicsExtension(moveStat);
            __result = __instance.CalculateGraphicsHash(graphicsExt, pawn.Rotation, pawn.CurRotDrawMode()) == ___cachedMatsBodyBaseHash;
            return false;
        }
    }

    [HarmonyPatch(typeof(PawnGraphicSet), "ResolveAllGraphics")]
    public class TerrainAwarePawnGraphicResolveAllGraphics
    {
        static void Postfix(Pawn ___pawn, ref PawnGraphicSet __instance)
        {
            Pawn pawn = ___pawn;
            StatDef moveStat = pawn.BestTerrainMoveStat();
            if (!(moveStat == null || moveStat == StatDefOf.MoveSpeed))
            {
                TerrainMovementPawnKindGraphics graphicsExt = pawn.LoadTerrainMovementPawnKindGraphicsExtension(moveStat);
                if (graphicsExt != null)
                {
                    // Resolve the graphics classes the first time they are reached
                    if (graphicsExt.bodyGraphicData != null && graphicsExt.bodyGraphicData.graphicClass == null)
                    {
                        graphicsExt.bodyGraphicData.graphicClass = typeof(Graphic_Multi);
                    }
                    if (graphicsExt.femaleGraphicData != null && graphicsExt.femaleGraphicData.graphicClass == null)
                    {
                        graphicsExt.femaleGraphicData.graphicClass = typeof(Graphic_Multi);
                    }

                    // Pick the graphic geing used
                    if (pawn.gender != Gender.Female || graphicsExt.femaleGraphicData == null)
                    {
                        __instance.nakedGraphic = graphicsExt.bodyGraphicData.Graphic;
                    }
                    else
                    {
                        __instance.nakedGraphic = graphicsExt.femaleGraphicData.Graphic;
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(PawnGraphicSet), nameof(PawnGraphicSet.MatsBodyBaseAt))]
    public static class MatsBodyBaseAt
    {
        static readonly MethodInfo CalculateGraphicsHashInfo = AccessTools.Method(typeof(PawnGraphicSetExtension), nameof(PawnGraphicSetExtension.CalculateGraphicsHash));
        static readonly MethodInfo BestTerrainMoveStatInfo = AccessTools.Method(typeof(PawnExtensions), nameof(PawnExtensions.BestTerrainMoveStat));
        static readonly MethodInfo LoadTerrainMovementPawnKindGraphicsExtensionInfo = AccessTools.Method(typeof(PawnExtensions), nameof(PawnExtensions.LoadTerrainMovementPawnKindGraphicsExtension));
        static readonly FieldInfo PawnGraphicSetPawnField = AccessTools.Field(typeof(PawnGraphicSet), nameof(PawnGraphicSet.pawn));

        /*
         * Replaces
         * 
         * `int num = facing.AsInt + 1000 * (int)bodyCondition;`
         * 
         * with
         * 
         * ```
         * int num = this.CalculateGraphicsHash(
         *   pawn.LoadTerrainMovementPawnKindGraphicsExtension(pawn.BestTerrainMoveStat()),
         *   facing,
         *   bodyCondition);
         * ```
         */
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var instructionList = instructions.ToList();
            bool found = false;
            foreach (var instruction in instructionList)
            {
                if (!found)
                {
                    // Find the num = assignment
                    if (instruction.opcode == OpCodes.Stloc_0)
                    {
                        found = true;

                        // this arg for CalculateGraphicsHash
                        yield return new CodeInstruction(OpCodes.Ldarg_0); // PawnGraphicsSet

                        // Push MovementGraphics Extension onto the stack
                        yield return new CodeInstruction(OpCodes.Ldarg_0); // PawnGraphicsSet
                        yield return new CodeInstruction(OpCodes.Ldfld, PawnGraphicSetPawnField); // this.pawn
                        // Push moveStat onto the stack
                        yield return new CodeInstruction(OpCodes.Ldarg_0); // PawnGraphicsSet
                        yield return new CodeInstruction(OpCodes.Ldfld, PawnGraphicSetPawnField); // this.pawn
                        yield return new CodeInstruction(OpCodes.Callvirt, BestTerrainMoveStatInfo); // pawn.BestTerrainMoveStatInfo
                        // All args on stack for Load call
                        yield return new CodeInstruction(OpCodes.Callvirt, LoadTerrainMovementPawnKindGraphicsExtensionInfo); // pawn.LoadTerrainMovementPawnKindGraphicsExtension

                        // Push facing arg onto the stack
                        yield return new CodeInstruction(OpCodes.Ldarg_1);

                        // Push rot arg onto the stack
                        yield return new CodeInstruction(OpCodes.Ldarg_2);

                        // Call our new method
                        yield return new CodeInstruction(OpCodes.Callvirt, CalculateGraphicsHashInfo);
                        yield return instruction; // Use original assignment into `num`
                    }
                }
                else
                {
                    yield return instruction;
                }
            }
            if (!found)
            {
                Log.ErrorOnce("[TerrainKit] Could not patch 'PawnGraphicSet.MatsBodyBaseAt', skipping patch attempt.", CalculateGraphicsHashInfo.GetHashCode());
                foreach (var instruction in instructionList)
                {
                    yield return instruction;
                }
            }
        }

    }
}
