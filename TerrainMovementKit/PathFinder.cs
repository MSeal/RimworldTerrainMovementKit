﻿using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Verse;
using Verse.AI;
using HarmonyLib;

namespace TerrainMovement
{
	
    [HarmonyPatch(typeof(PathFinder), "FindPath", new Type[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(TraverseParms), typeof(PathEndMode), typeof(PathFinderCostTuning) })]
    public class TerrainPathPatch
    {
        static bool Prefix(ref PawnPath __result, Map ___map, IntVec3 start, LocalTargetInfo dest, TraverseParms traverseParms, PathEndMode peMode, PathFinderCostTuning tuning)
        {
            __result = ___map.TerrainAwarePather().FindPath(start, dest, traverseParms, peMode, tuning);
            return false;
        }
    }

    /*
     * It was easier to copy the ENTIRE class to be able to overwrite the giant FindPath method correctly
     */
    public class TerrainAwarePathFinder
	{
		protected internal struct CostNode
		{
			public int index;

			public int cost;

			public CostNode(int index, int cost)
			{
				this.index = index;
				this.cost = cost;
			}
		}

		protected struct PathFinderNodeFast
		{
			public int knownCost;

			public int heuristicCost;

			public int parentIndex;

			public int costNodeCost;

			public ushort status;
		}

		protected internal class CostNodeComparer : IComparer<CostNode>
		{
			public int Compare(CostNode a, CostNode b)
			{
				return a.cost.CompareTo(b.cost);
			}
		}

		protected Map map;

		protected FastPriorityQueue<CostNode> openList;

		protected static PathFinderNodeFast[] calcGrid;

		protected static ushort statusOpenValue = 1;

		protected static ushort statusClosedValue = 2;

		protected RegionCostCalculatorWrapper regionCostCalculator;

		protected int mapSizeX;

		protected int mapSizeZ;

		protected PathGrid pathGrid;

		protected TraverseParms traverseParms;

		protected PathingContext pathingContext;

		protected Building[] edificeGrid;

		protected List<Blueprint>[] blueprintGrid;

		protected CellIndices cellIndices;

		protected List<int> disallowedCornerIndices = new List<int>(4);

		public const int DefaultMoveTicksCardinal = 13;

		protected const int DefaultMoveTicksDiagonal = 18;

		protected const int SearchLimit = 160000;

		protected static readonly int[] Directions = new int[16]
		{
			0,
			1,
			0,
			-1,
			1,
			1,
			-1,
			-1,
			-1,
			0,
			1,
			0,
			-1,
			1,
			1,
			-1
		};

		protected const int Cost_DoorToBash = 300;

		protected const int Cost_FenceToBash = 70;

		public const int Cost_OutsideAllowedArea = 600;

		protected const int Cost_PawnCollision = 175;

		protected const int NodesToOpenBeforeRegionBasedPathing_NonColonist = 2000;

		protected const int NodesToOpenBeforeRegionBasedPathing_Colonist = 100000;

		protected const float NonRegionBasedHeuristicStrengthAnimal = 1.75f;

		protected static readonly SimpleCurve NonRegionBasedHeuristicStrengthHuman_DistanceCurve = new SimpleCurve
		{
			new CurvePoint(40f, 1f),
			new CurvePoint(120f, 2.8f)
		};

		protected static readonly SimpleCurve RegionHeuristicWeightByNodesOpened = new SimpleCurve
		{
			new CurvePoint(0f, 1f),
			new CurvePoint(3500f, 1f),
			new CurvePoint(4500f, 5f),
			new CurvePoint(30000f, 50f),
			new CurvePoint(100000f, 500f)
		};

		public TerrainAwarePathFinder(Map map)
		{
			this.map = map;
			mapSizeX = map.Size.x;
			mapSizeZ = map.Size.z;
			int num = mapSizeX * mapSizeZ;
			if (calcGrid == null || calcGrid.Length < num)
			{
				calcGrid = new PathFinderNodeFast[num];
			}
			openList = new FastPriorityQueue<CostNode>(new CostNodeComparer());
			regionCostCalculator = new RegionCostCalculatorWrapper(map);
		}

		public PawnPath FindPath(IntVec3 start, LocalTargetInfo dest, Pawn pawn, PathEndMode peMode = PathEndMode.OnCell, PathFinderCostTuning tuning = null)
		{
			bool canBashDoors = false;
			bool canBashFences = false;
			if (pawn?.CurJob != null)
			{
				if (pawn.CurJob.canBashDoors)
				{
					canBashDoors = true;
				}
				if (pawn.CurJob.canBashFences)
				{
					canBashFences = true;
				}
			}
			return FindPath(start, dest, TraverseParms.For(pawn, Danger.Deadly, TraverseMode.ByPawn, canBashDoors, alwaysUseAvoidGrid: false, canBashFences), peMode, tuning);
		}

		public PawnPath FindPath(IntVec3 start, LocalTargetInfo dest, TraverseParms traverseParms, PathEndMode peMode = PathEndMode.OnCell, PathFinderCostTuning tuning = null)
		{
			if (DebugSettings.pathThroughWalls)
			{
				traverseParms.mode = TraverseMode.PassAllDestroyableThings;
			}
			Pawn pawn = traverseParms.pawn;
			if (pawn != null && pawn.Map != map)
			{
				Log.Warning(String.Format("Map object was rebuilt, but new pather was not assigned to new map (new {0}) (old {1})", pawn.Map.uniqueID, map.uniqueID));
				Log.Error("Tried to FindPath for pawn which is spawned in another map. His map PathFinder should have been used, not this one. pawn=" + pawn + " pawn.Map=" + pawn.Map + " map=" + map);
				return PawnPath.NotFound;
			}
			if (!start.IsValid)
			{
				Log.Error("Tried to FindPath with invalid start " + start + ", pawn= " + pawn);
				return PawnPath.NotFound;
			}
			if (!dest.IsValid)
			{
				Log.Error("Tried to FindPath with invalid dest " + dest + ", pawn= " + pawn);
				return PawnPath.NotFound;
			}
			if (traverseParms.mode == TraverseMode.ByPawn)
			{
				if (!pawn.CanReach(dest, peMode, Danger.Deadly, traverseParms.canBashDoors, traverseParms.canBashFences, traverseParms.mode))
				{
					return PawnPath.NotFound;
				}
			}
			else if (!map.reachability.CanReach(start, dest, peMode, traverseParms))
			{
				return PawnPath.NotFound;
			}
			PfProfilerBeginSample("FindPath for " + pawn + " from " + start + " to " + dest + (dest.HasThing ? (" at " + dest.Cell) : ""));
			cellIndices = map.cellIndices;
			pathingContext = map.pathing.For(traverseParms);
			pathGrid = pathingContext.pathGrid;
			this.traverseParms = traverseParms;
			this.edificeGrid = map.edificeGrid.InnerArray;
			blueprintGrid = map.blueprintGrid.InnerArray;
			int x = dest.Cell.x;
			int z = dest.Cell.z;
			int curIndex = cellIndices.CellToIndex(start);
			int num = cellIndices.CellToIndex(dest.Cell);
			ByteGrid byteGrid = traverseParms.alwaysUseAvoidGrid ? map.avoidGrid.Grid : pawn?.GetAvoidGrid();
			bool flag = traverseParms.mode == TraverseMode.PassAllDestroyableThings || traverseParms.mode == TraverseMode.PassAllDestroyableThingsNotWater;
			bool flag2 = traverseParms.mode != TraverseMode.NoPassClosedDoorsOrWater && traverseParms.mode != TraverseMode.PassAllDestroyableThingsNotWater;
			bool flag3 = !flag;
			CellRect cellRect = CalculateDestinationRect(dest, peMode);
			bool flag4 = cellRect.Width == 1 && cellRect.Height == 1;
			int[] array = pathGrid.pathGrid;
			TerrainDef[] topGrid = map.terrainGrid.topGrid;
			EdificeGrid edificeGrid = map.edificeGrid;
			int num2 = 0;
			int num3 = 0;
			Area allowedArea = GetAllowedArea(pawn);
			BoolGrid lordWalkGrid = GetLordWalkGrid(pawn);
			bool flag5 = pawn != null && PawnUtility.ShouldCollideWithPawns(pawn);
			bool flag6 = (!flag && start.GetRegion(map) != null) & flag2;
			bool flag7 = !flag || !flag3;
			bool flag8 = false;
			bool flag9 = pawn?.Drafted ?? false;
			int num4 = (pawn?.IsColonist ?? false) ? 100000 : 2000;
			tuning = (tuning ?? PathFinderCostTuning.DefaultTuning);
			int costBlockedWallBase = tuning.costBlockedWallBase;
			float costBlockedWallExtraPerHitPoint = tuning.costBlockedWallExtraPerHitPoint;
			int costOffLordWalkGrid = tuning.costOffLordWalkGrid;
			int num5 = 0;
			int num6 = 0;
			float num7 = DetermineHeuristicStrength(pawn, start, dest);
			// BEGIN CHANGED SECTION
			// In case pawn is null
			int num8 = 13;
			int num9 = 18;
			Dictionary<TerrainDef, int> pawnTerrainCacheCardinal = new Dictionary<TerrainDef, int>();
			Dictionary<TerrainDef, int> pawnTerrainCacheDiagonal = new Dictionary<TerrainDef, int>();
			Dictionary<TerrainDef, bool> pawnSpecialMovementCache = new Dictionary<TerrainDef, bool>();
			Dictionary<TerrainDef, bool> pawnImpassibleMovementCache = new Dictionary<TerrainDef, bool>();
			Dictionary<TerrainDef, int> pawnTerrainMovementCache = new Dictionary<TerrainDef, int>();
			// END CHANGED SECTION
			CalculateAndAddDisallowedCorners(peMode, cellRect);
			InitStatusesAndPushStartNode(ref curIndex, start);
			while (true)
			{
				PfProfilerBeginSample("Open cell");
				if (openList.Count <= 0)
				{
					string text = (pawn != null && pawn.CurJob != null) ? pawn.CurJob.ToString() : "null";
					string text2 = (pawn != null && pawn.Faction != null) ? pawn.Faction.ToString() : "null";
					Log.Warning(pawn + " pathing from " + start + " to " + dest + " ran out of cells to process.\nJob:" + text + "\nFaction: " + text2);
					DebugDrawRichData();
					PfProfilerEndSample();
					PfProfilerEndSample();
					return PawnPath.NotFound;
				}
				num5 += openList.Count;
				num6++;
				CostNode costNode = openList.Pop();
				curIndex = costNode.index;
				if (costNode.cost != calcGrid[curIndex].costNodeCost)
				{
					PfProfilerEndSample();
					continue;
				}
				if (calcGrid[curIndex].status == statusClosedValue)
				{
					PfProfilerEndSample();
					continue;
				}
				IntVec3 intVec = cellIndices.IndexToCell(curIndex);
				int x2 = intVec.x;
				int z2 = intVec.z;
				if (flag4)
				{
					if (curIndex == num)
					{
						PfProfilerEndSample();
						PawnPath result = FinalizedPath(curIndex, flag8);
						PfProfilerEndSample();
						return result;
					}
				}
				else if (cellRect.Contains(intVec) && !disallowedCornerIndices.Contains(curIndex))
				{
					PfProfilerEndSample();
					PawnPath result2 = FinalizedPath(curIndex, flag8);
					PfProfilerEndSample();
					return result2;
				}
				if (num2 > 160000)
				{
					break;
				}
				PfProfilerEndSample();
				PfProfilerBeginSample("Neighbor consideration");
				for (int i = 0; i < 8; i++)
				{
					uint num10 = (uint)(x2 + Directions[i]);
					uint num11 = (uint)(z2 + Directions[i + 8]);
					if (num10 >= mapSizeX || num11 >= mapSizeZ)
					{
						continue;
					}
					int num12 = (int)num10;
					int num13 = (int)num11;
					int num14 = cellIndices.CellToIndex(num12, num13);
					if (calcGrid[num14].status == statusClosedValue && !flag8)
					{
						continue;
					}
					// BEGIN CHANGED SECTION
					IntVec3 targetCell = new IntVec3(num12, 0, num13);
					TerrainDef targetTerrain = topGrid[num14];
					if (pawn != null)
					{
						// Use cache of terrain movement indicators to avoid a lot of repeated computation
						if (!pawnImpassibleMovementCache.TryGetValue(targetTerrain, out bool impassible))
						{
							impassible = pawn.kindDef.UnreachableTerrainCheck(targetTerrain);
							pawnImpassibleMovementCache[targetTerrain] = impassible;
						}
						if (impassible)
						{
							// Skip this cell for pathing calculations
							calcGrid[num14].status = statusClosedValue;
							continue;
						}

						// Overwrite directional move costs
						if (!pawnTerrainCacheCardinal.TryGetValue(targetTerrain, out num8))
						{
							num8 = pawn.TerrainAwareTicksPerMoveCardinal(targetTerrain);
							pawnTerrainCacheCardinal[targetTerrain] = num8;
						}
						if (!pawnTerrainCacheDiagonal.TryGetValue(targetTerrain, out num9))
						{
							num9 = pawn.TerrainAwareTicksPerMoveDiagonal(targetTerrain);
							pawnTerrainCacheDiagonal[targetTerrain] = num9;
						}
					}
					// END CHANGED SECTION
					int num15 = 0;
					bool flag10 = false;
					//if (!flag2 && new IntVec3(num12, 0, num13).GetTerrain(map).HasTag("Water"))
					if (!flag2 && targetTerrain.HasTag("Water"))
					{
						continue;
					}
					if (!pathGrid.WalkableFast(num14))
					{
						if (!flag)
						{
							continue;
						}
						flag10 = true;
						num15 += costBlockedWallBase;
						Building building = edificeGrid[num14];
						if (building == null || !IsDestroyable(building))
						{
							continue;
						}
						num15 += (int)((float)building.HitPoints * costBlockedWallExtraPerHitPoint);
					}
					switch (i)
					{
						case 4:
							if (BlocksDiagonalMovement(curIndex - mapSizeX))
							{
								if (flag7)
								{
									continue;
								}
								num15 += costBlockedWallBase;
							}
							if (BlocksDiagonalMovement(curIndex + 1))
							{
								if (flag7)
								{
									continue;
								}
								num15 += costBlockedWallBase;
							}
							break;
						case 5:
							if (BlocksDiagonalMovement(curIndex + mapSizeX))
							{
								if (flag7)
								{
									continue;
								}
								num15 += costBlockedWallBase;
							}
							if (BlocksDiagonalMovement(curIndex + 1))
							{
								if (flag7)
								{
									continue;
								}
								num15 += costBlockedWallBase;
							}
							break;
						case 6:
							if (BlocksDiagonalMovement(curIndex + mapSizeX))
							{
								if (flag7)
								{
									continue;
								}
								num15 += costBlockedWallBase;
							}
							if (BlocksDiagonalMovement(curIndex - 1))
							{
								if (flag7)
								{
									continue;
								}
								num15 += costBlockedWallBase;
							}
							break;
						case 7:
							if (BlocksDiagonalMovement(curIndex - mapSizeX))
							{
								if (flag7)
								{
									continue;
								}
								num15 += costBlockedWallBase;
							}
							if (BlocksDiagonalMovement(curIndex - 1))
							{
								if (flag7)
								{
									continue;
								}
								num15 += costBlockedWallBase;
							}
							break;
					}
					int num16 = (i > 3) ? num9 : num8;
					num16 += num15;
					if (!flag10)
					{
						// BEGIN CHANGED SECTION
						//num16 += array[num14];
						//num16 = ((!flag9) ? (num16 + topGrid[num14].extraNonDraftedPerceivedPathCost) : (num16 + topGrid[num14].extraDraftedPerceivedPathCost));
						if (pawn == null)
						{
							num16 += array[num14];
							if (flag9)
							{
								num16 += targetTerrain.extraDraftedPerceivedPathCost;
							}
							else
							{
								num16 += targetTerrain.extraNonDraftedPerceivedPathCost;
							}
						}
						else
						{
							// Use cache of terrain perceived cost instead of fixed pathCost grid to avoid a lot of repeated computation while maintaining accuracy
							if (!pawnTerrainMovementCache.TryGetValue(targetTerrain, out int terrainMoveCost))
							{
								terrainMoveCost = pawn.TerrainMoveCost(targetTerrain);
								pawnTerrainMovementCache[targetTerrain] = terrainMoveCost;
							}
							// This was really really expensive, so we opted to mod this pre-calced value
							//num16 += pathGrid.TerrainCalculatedCostAt(map, pawn, targetCell, true, IntVec3.Invalid, terrainMoveCost);
							num16 += pathGrid.ApplyTerrainModToCalculatedCost(targetTerrain, array[num14], terrainMoveCost);
							// Use cache of terrain movement indicators to avoid a lot of repeated computation
							if (!pawnSpecialMovementCache.TryGetValue(targetTerrain, out bool specialMovement))
							{
								specialMovement = pawn.TerrainMoveStat(targetTerrain) != StatDefOf.MoveSpeed;
								pawnSpecialMovementCache[targetTerrain] = specialMovement;
							}
							// Skip applying the PerceivedPathCost hack if we've got a specialized speed stat for this terrain
							if (!specialMovement)
							{
								if (flag9)
								{
									num16 += targetTerrain.extraDraftedPerceivedPathCost;
								}
								else
								{
									num16 += targetTerrain.extraNonDraftedPerceivedPathCost;
								}
							}
						}
						// END CHANGED SECTION
					}
					if (byteGrid != null)
					{
						num16 += byteGrid[num14] * 8;
					}
					if (allowedArea != null && !allowedArea[num14])
					{
						num16 += 600;
					}
					//new IntVec3(num12, 0, num13) -> targetCell
					if (flag5 && PawnUtility.AnyPawnBlockingPathAt(targetCell, pawn, actAsIfHadCollideWithPawnsJob: false, collideOnlyWithStandingPawns: false, forPathFinder: true))
					{
						num16 += 175;
					}
					Building building2 = this.edificeGrid[num14];
					if (building2 != null)
					{
						PfProfilerBeginSample("Edifices");
						int buildingCost = GetBuildingCost(building2, traverseParms, pawn, tuning);
						if (buildingCost == int.MaxValue)
						{
							PfProfilerEndSample();
							continue;
						}
						num16 += buildingCost;
						PfProfilerEndSample();
					}
					List<Blueprint> list = blueprintGrid[num14];
					if (list != null)
					{
						PfProfilerBeginSample("Blueprints");
						int num17 = 0;
						for (int j = 0; j < list.Count; j++)
						{
							num17 = Mathf.Max(num17, GetBlueprintCost(list[j], pawn));
						}
						if (num17 == int.MaxValue)
						{
							PfProfilerEndSample();
							continue;
						}
						num16 += num17;
						PfProfilerEndSample();
					}
					if (tuning.custom != null)
					{
						num16 += tuning.custom.CostOffset(intVec, new IntVec3(num12, 0, num13));
					}
					if (lordWalkGrid != null && !lordWalkGrid[new IntVec3(num12, 0, num13)])
					{
						num16 += costOffLordWalkGrid;
					}
					int num18 = num16 + calcGrid[curIndex].knownCost;
					ushort status = calcGrid[num14].status;
					if (status == statusClosedValue || status == statusOpenValue)
					{
						int num19 = 0;
						if (status == statusClosedValue)
						{
							num19 = num8;
						}
						if (calcGrid[num14].knownCost <= num18 + num19)
						{
							continue;
						}
					}
					if (flag8)
					{
						calcGrid[num14].heuristicCost = Mathf.RoundToInt((float)regionCostCalculator.GetPathCostFromDestToRegion(num14) * RegionHeuristicWeightByNodesOpened.Evaluate(num3));
						if (calcGrid[num14].heuristicCost < 0)
						{
							Log.ErrorOnce("Heuristic cost overflow for " + pawn.ToStringSafe() + " pathing from " + start + " to " + dest + ".", pawn.GetHashCode() ^ 0xB8DC389);
							calcGrid[num14].heuristicCost = 0;
						}
					}
					else if (status != statusClosedValue && status != statusOpenValue)
					{
						int dx = Math.Abs(num12 - x);
						int dz = Math.Abs(num13 - z);
						int num20 = GenMath.OctileDistance(dx, dz, num8, num9);
						calcGrid[num14].heuristicCost = Mathf.RoundToInt((float)num20 * num7);
					}
					int num21 = num18 + calcGrid[num14].heuristicCost;
					if (num21 < 0)
					{
						Log.ErrorOnce("Node cost overflow for " + pawn.ToStringSafe() + " pathing from " + start + " to " + dest + ".", pawn.GetHashCode() ^ 0x53CB9DE);
						num21 = 0;
					}
					calcGrid[num14].parentIndex = curIndex;
					calcGrid[num14].knownCost = num18;
					calcGrid[num14].status = statusOpenValue;
					calcGrid[num14].costNodeCost = num21;
					num3++;
					openList.Push(new CostNode(num14, num21));
				}
				PfProfilerEndSample();
				num2++;
				calcGrid[curIndex].status = statusClosedValue;
				if (num3 >= num4 && flag6 && !flag8)
				{
					flag8 = true;
					regionCostCalculator.Init(cellRect, traverseParms, num8, num9, byteGrid, allowedArea, flag9, disallowedCornerIndices);
					InitStatusesAndPushStartNode(ref curIndex, start);
					num3 = 0;
					num2 = 0;
				}
			}
			Log.Warning(pawn + " pathing from " + start + " to " + dest + " hit search limit of " + 160000 + " cells.");
			DebugDrawRichData();
			PfProfilerEndSample();
			PfProfilerEndSample();
			return PawnPath.NotFound;
		}

		public static int GetBuildingCost(Building b, TraverseParms traverseParms, Pawn pawn, PathFinderCostTuning tuning = null)
		{
			tuning = (tuning ?? PathFinderCostTuning.DefaultTuning);
			int costBlockedDoor = tuning.costBlockedDoor;
			float costBlockedDoorPerHitPoint = tuning.costBlockedDoorPerHitPoint;
			Building_Door building_Door = b as Building_Door;
			if (building_Door != null)
			{
				switch (traverseParms.mode)
				{
					case TraverseMode.NoPassClosedDoors:
					case TraverseMode.NoPassClosedDoorsOrWater:
						if (building_Door.FreePassage)
						{
							return 0;
						}
						return int.MaxValue;
					case TraverseMode.PassAllDestroyableThings:
					case TraverseMode.PassAllDestroyableThingsNotWater:
						if (pawn != null && building_Door.PawnCanOpen(pawn) && !building_Door.IsForbiddenToPass(pawn) && !building_Door.FreePassage)
						{
							return building_Door.TicksToOpenNow;
						}
						if ((pawn != null && building_Door.CanPhysicallyPass(pawn)) || building_Door.FreePassage)
						{
							return 0;
						}
						return costBlockedDoor + (int)((float)building_Door.HitPoints * costBlockedDoorPerHitPoint);
					case TraverseMode.PassDoors:
						if (pawn != null && building_Door.PawnCanOpen(pawn) && !building_Door.IsForbiddenToPass(pawn) && !building_Door.FreePassage)
						{
							return building_Door.TicksToOpenNow;
						}
						if ((pawn != null && building_Door.CanPhysicallyPass(pawn)) || building_Door.FreePassage)
						{
							return 0;
						}
						return 150;
					case TraverseMode.ByPawn:
						if (!traverseParms.canBashDoors && building_Door.IsForbiddenToPass(pawn))
						{
							return int.MaxValue;
						}
						if (building_Door.PawnCanOpen(pawn) && !building_Door.FreePassage)
						{
							return building_Door.TicksToOpenNow;
						}
						if (building_Door.CanPhysicallyPass(pawn))
						{
							return 0;
						}
						if (traverseParms.canBashDoors)
						{
							return 300;
						}
						return int.MaxValue;
				}
			}
			else if (b.def.IsFence && traverseParms.fenceBlocked)
			{
				switch (traverseParms.mode)
				{
					case TraverseMode.ByPawn:
						if (traverseParms.canBashFences)
						{
							return 300;
						}
						return int.MaxValue;
					case TraverseMode.PassAllDestroyableThings:
					case TraverseMode.PassAllDestroyableThingsNotWater:
						return costBlockedDoor + (int)((float)b.HitPoints * costBlockedDoorPerHitPoint);
					case TraverseMode.PassDoors:
					case TraverseMode.NoPassClosedDoors:
					case TraverseMode.NoPassClosedDoorsOrWater:
						return 0;
				}
			}
			else if (pawn != null)
			{
				return b.PathFindCostFor(pawn);
			}
			return 0;
		}

		public static int GetBlueprintCost(Blueprint b, Pawn pawn)
		{
			if (pawn != null)
			{
				return b.PathFindCostFor(pawn);
			}
			return 0;
		}

		public static bool IsDestroyable(Thing th)
		{
			if (th.def.useHitPoints)
			{
				return th.def.destroyable;
			}
			return false;
		}

		protected bool BlocksDiagonalMovement(int index)
		{
			return BlocksDiagonalMovement(index, pathingContext, traverseParms.canBashFences);
		}

		public static bool BlocksDiagonalMovement(int x, int z, PathingContext pc, bool canBashFences)
		{
			return BlocksDiagonalMovement(pc.map.cellIndices.CellToIndex(x, z), pc, canBashFences);
		}

		public static bool BlocksDiagonalMovement(int index, PathingContext pc, bool canBashFences)
		{
			if (!pc.pathGrid.WalkableFast(index))
			{
				return true;
			}
			Building building = pc.map.edificeGrid[index];
			if (building != null)
			{
				if (building is Building_Door)
				{
					return true;
				}
				if (canBashFences && building.def.IsFence)
				{
					return true;
				}
			}
			return false;
		}

		protected void DebugFlash(IntVec3 c, float colorPct, string str)
		{
			DebugFlash(c, map, colorPct, str);
		}

		protected static void DebugFlash(IntVec3 c, Map map, float colorPct, string str)
		{
			map.debugDrawer.FlashCell(c, colorPct, str);
		}

		protected PawnPath FinalizedPath(int finalIndex, bool usedRegionHeuristics)
		{
			PawnPath emptyPawnPath = map.pawnPathPool.GetEmptyPawnPath();
			int num = finalIndex;
			while (true)
			{
				int parentIndex = calcGrid[num].parentIndex;
				emptyPawnPath.AddNode(map.cellIndices.IndexToCell(num));
				if (num == parentIndex)
				{
					break;
				}
				num = parentIndex;
			}
			emptyPawnPath.SetupFound(calcGrid[finalIndex].knownCost, usedRegionHeuristics);
			return emptyPawnPath;
		}

		protected void InitStatusesAndPushStartNode(ref int curIndex, IntVec3 start)
		{
			statusOpenValue += 2;
			statusClosedValue += 2;
			if (statusClosedValue >= 65435)
			{
				ResetStatuses();
			}
			curIndex = cellIndices.CellToIndex(start);
			calcGrid[curIndex].knownCost = 0;
			calcGrid[curIndex].heuristicCost = 0;
			calcGrid[curIndex].costNodeCost = 0;
			calcGrid[curIndex].parentIndex = curIndex;
			calcGrid[curIndex].status = statusOpenValue;
			openList.Clear();
			openList.Push(new CostNode(curIndex, 0));
		}

		protected void ResetStatuses()
		{
			int num = calcGrid.Length;
			for (int i = 0; i < num; i++)
			{
				calcGrid[i].status = 0;
			}
			statusOpenValue = 1;
			statusClosedValue = 2;
		}

		[Conditional("PFPROFILE")]
		protected void PfProfilerBeginSample(string s)
		{
		}

		[Conditional("PFPROFILE")]
		protected void PfProfilerEndSample()
		{
		}

		protected void DebugDrawRichData()
		{
		}

		protected float DetermineHeuristicStrength(Pawn pawn, IntVec3 start, LocalTargetInfo dest)
		{
			if (pawn != null && pawn.RaceProps.Animal)
			{
				return 1.75f;
			}
			float lengthHorizontal = (start - dest.Cell).LengthHorizontal;
			return Mathf.RoundToInt(NonRegionBasedHeuristicStrengthHuman_DistanceCurve.Evaluate(lengthHorizontal));
		}

		protected CellRect CalculateDestinationRect(LocalTargetInfo dest, PathEndMode peMode)
		{
			CellRect result = (dest.HasThing && peMode != PathEndMode.OnCell) ? dest.Thing.OccupiedRect() : CellRect.SingleCell(dest.Cell);
			if (peMode == PathEndMode.Touch)
			{
				result = result.ExpandedBy(1);
			}
			return result;
		}

		protected Area GetAllowedArea(Pawn pawn)
		{
			if (pawn != null && pawn.playerSettings != null && !pawn.Drafted && ForbidUtility.CaresAboutForbidden(pawn, cellTarget: true))
			{
				Area area = pawn.playerSettings.EffectiveAreaRestrictionInPawnCurrentMap;
				if (area != null && area.TrueCount <= 0)
				{
					area = null;
				}
				return area;
			}
			return null;
		}
		private BoolGrid GetLordWalkGrid(Pawn pawn)
		{
			return BreachingUtility.BreachingGridFor(pawn)?.WalkGrid;
		}

		protected void CalculateAndAddDisallowedCorners(PathEndMode peMode, CellRect destinationRect)
		{
			disallowedCornerIndices.Clear();
			if (peMode == PathEndMode.Touch)
			{
				int minX = destinationRect.minX;
				int minZ = destinationRect.minZ;
				int maxX = destinationRect.maxX;
				int maxZ = destinationRect.maxZ;
				if (!IsCornerTouchAllowed(minX + 1, minZ + 1, minX + 1, minZ, minX, minZ + 1))
				{
					disallowedCornerIndices.Add(map.cellIndices.CellToIndex(minX, minZ));
				}
				if (!IsCornerTouchAllowed(minX + 1, maxZ - 1, minX + 1, maxZ, minX, maxZ - 1))
				{
					disallowedCornerIndices.Add(map.cellIndices.CellToIndex(minX, maxZ));
				}
				if (!IsCornerTouchAllowed(maxX - 1, maxZ - 1, maxX - 1, maxZ, maxX, maxZ - 1))
				{
					disallowedCornerIndices.Add(map.cellIndices.CellToIndex(maxX, maxZ));
				}
				if (!IsCornerTouchAllowed(maxX - 1, minZ + 1, maxX - 1, minZ, maxX, minZ + 1))
				{
					disallowedCornerIndices.Add(map.cellIndices.CellToIndex(maxX, minZ));
				}
			}
		}

		protected bool IsCornerTouchAllowed(int cornerX, int cornerZ, int adjCardinal1X, int adjCardinal1Z, int adjCardinal2X, int adjCardinal2Z)
		{
			return TouchPathEndModeUtility.IsCornerTouchAllowed(cornerX, cornerZ, adjCardinal1X, adjCardinal1Z, adjCardinal2X, adjCardinal2Z, pathingContext);
		}
	}
}
