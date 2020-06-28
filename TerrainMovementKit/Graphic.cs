using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using HarmonyLib;
using UnityEngine;

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
            if (moveStat == null)
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
            if (moveStat != null)
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

    // TODO: Transpile the num calculation
    [HarmonyPatch(typeof(PawnGraphicSet), "MatsBodyBaseAt", new Type[] { typeof(Rot4), typeof(RotDrawMode) })]
    public class TerrainAwarePawnGraphicSetMovementTypeCheck
    {
        static bool Prefix(ref List<Material> __result, Pawn ___pawn, ref PawnGraphicSet __instance, ref int ___cachedMatsBodyBaseHash, ref List<Material> ___cachedMatsBodyBase, ref List<ApparelGraphicRecord> ___apparelGraphics, Rot4 facing, RotDrawMode bodyCondition)
        {
            Pawn pawn = ___pawn;
            StatDef moveStat = pawn.BestTerrainMoveStat();
            // Only support RotDrawMode.Fresh renderings for now
            if (moveStat != null && bodyCondition == RotDrawMode.Fresh)
            {
                TerrainMovementPawnKindGraphics graphicsExt = pawn.LoadTerrainMovementPawnKindGraphicsExtension(moveStat);
                // Rerun a subset of the logic from MatsBodyBaseAt
                int num = __instance.CalculateGraphicsHash(graphicsExt, pawn.Rotation, pawn.CurRotDrawMode());
                if (num != ___cachedMatsBodyBaseHash)
                {
                    ___cachedMatsBodyBase.Clear();
                    ___cachedMatsBodyBaseHash = num;
                    ___cachedMatsBodyBase.Add(__instance.nakedGraphic.MatAt(facing));
                    for (int i = 0; i < ___apparelGraphics.Count; i++)
                    {
                        if (___apparelGraphics[i].sourceApparel.def.apparel.LastLayer != ApparelLayerDefOf.Shell && ___apparelGraphics[i].sourceApparel.def.apparel.LastLayer != ApparelLayerDefOf.Overhead)
                        {
                            ___cachedMatsBodyBase.Add(___apparelGraphics[i].graphic.MatAt(facing));
                        }
                    }
                }
                __result = ___cachedMatsBodyBase;
                return false;
            }
            return true;
        }
    }
}
