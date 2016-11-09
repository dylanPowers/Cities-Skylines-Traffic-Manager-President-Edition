﻿using ColossalFramework;
using System;
using System.Collections.Generic;
using System.Text;
using TrafficManager.State;
using TrafficManager.Util;
using UnityEngine;
using static ColossalFramework.UI.UITextureAtlas;

namespace TrafficManager.Manager {
	public class SpeedLimitManager {
		private static SpeedLimitManager instance = null;

		public static SpeedLimitManager Instance() {
			if (instance == null)
				instance = new SpeedLimitManager();
			return instance;
		}

		private static readonly float MAX_SPEED = 6f; // 300 km/h
		private Dictionary<string, float[]> vanillaLaneSpeedLimitsByNetInfoName; // For each NetInfo (by name) and lane index: game default speed limit
		private Dictionary<string, List<string>> childNetInfoNamesByCustomizableNetInfoName; // For each NetInfo (by name): Parent NetInfo (name)
		private List<NetInfo> customizableNetInfos;

		internal Dictionary<string, int> CustomLaneSpeedLimitIndexByNetInfoName; // For each NetInfo (by name) and lane index: custom speed limit index
		internal Dictionary<string, NetInfo> NetInfoByName; // For each name: NetInfo

		static SpeedLimitManager() {
			Instance();
		}

		public readonly List<ushort> AvailableSpeedLimits;

		private SpeedLimitManager() {
			AvailableSpeedLimits = new List<ushort>();
			AvailableSpeedLimits.Add(10);
			AvailableSpeedLimits.Add(20);
			AvailableSpeedLimits.Add(30);
			AvailableSpeedLimits.Add(40);
			AvailableSpeedLimits.Add(50);
			AvailableSpeedLimits.Add(60);
			AvailableSpeedLimits.Add(70);
			AvailableSpeedLimits.Add(80);
			AvailableSpeedLimits.Add(90);
			AvailableSpeedLimits.Add(100);
			AvailableSpeedLimits.Add(110);
			AvailableSpeedLimits.Add(120);
			AvailableSpeedLimits.Add(130);
			AvailableSpeedLimits.Add(0);

			vanillaLaneSpeedLimitsByNetInfoName = new Dictionary<string, float[]>();
			CustomLaneSpeedLimitIndexByNetInfoName = new Dictionary<string, int>();
			customizableNetInfos = new List<NetInfo>();
			childNetInfoNamesByCustomizableNetInfoName = new Dictionary<string, List<string>>();
			NetInfoByName = new Dictionary<string, NetInfo>();
		}

		/// <summary>
		/// Determines the currently set speed limit for the given segment and lane direction in terms of discrete speed limit levels.
		/// An in-game speed limit of 2.0 (e.g. on highway) is hereby translated into a discrete speed limit value of 100 (km/h).
		/// </summary>
		/// <param name="segmentId"></param>
		/// <param name="finalDir"></param>
		/// <returns></returns>
		public ushort GetCustomSpeedLimit(ushort segmentId, NetInfo.Direction finalDir) {
#if TRACE
			Singleton<CodeProfiler>.instance.Start("SpeedLimitManager.GetCustomSpeedLimit1");
#endif
			// calculate the currently set mean speed limit
			if (segmentId == 0 || (Singleton<NetManager>.instance.m_segments.m_buffer[segmentId].m_flags & NetSegment.Flags.Created) == NetSegment.Flags.None) {
#if TRACE
				Singleton<CodeProfiler>.instance.Stop("SpeedLimitManager.GetCustomSpeedLimit1");
#endif
				return 0;
			}

			var segmentInfo = Singleton<NetManager>.instance.m_segments.m_buffer[segmentId].Info;
			uint curLaneId = Singleton<NetManager>.instance.m_segments.m_buffer[segmentId].m_lanes;
			int laneIndex = 0;
			float meanSpeedLimit = 0f;
			uint validLanes = 0;
			while (laneIndex < segmentInfo.m_lanes.Length && curLaneId != 0u) {
				NetInfo.Direction d = segmentInfo.m_lanes[laneIndex].m_finalDirection;
				if ((segmentInfo.m_lanes[laneIndex].m_laneType & (NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle)) == NetInfo.LaneType.None || d != finalDir)
					goto nextIter;

				ushort? setSpeedLimit = Flags.getLaneSpeedLimit(curLaneId);
				if (setSpeedLimit != null)
					meanSpeedLimit += ToGameSpeedLimit((ushort)setSpeedLimit); // custom speed limit
				else
					meanSpeedLimit += segmentInfo.m_lanes[laneIndex].m_speedLimit; // game default
				++validLanes;

				nextIter:
				curLaneId = Singleton<NetManager>.instance.m_lanes.m_buffer[curLaneId].m_nextLane;
				laneIndex++;
			}

			if (validLanes > 0)
				meanSpeedLimit /= (float)validLanes;
			ushort ret = LaneToCustomSpeedLimit(meanSpeedLimit);
#if TRACE
			Singleton<CodeProfiler>.instance.Stop("SpeedLimitManager.GetCustomSpeedLimit1");
#endif
			return ret;
		}

		/// <summary>
		/// Determines the average default speed limit for a given NetInfo object in terms of discrete speed limit levels.
		/// An in-game speed limit of 2.0 (e.g. on highway) is hereby translated into a discrete speed limit value of 100 (km/h).
		/// </summary>
		/// <param name="segmentInfo"></param>
		/// <param name="finalDir"></param>
		/// <returns></returns>
		public ushort GetAverageDefaultCustomSpeedLimit(NetInfo segmentInfo, NetInfo.Direction? finalDir=null) {
#if TRACE
			Singleton<CodeProfiler>.instance.Start("SpeedLimitManager.GetAverageDefaultCustomSpeedLimit");
#endif

			float meanSpeedLimit = 0f;
			uint validLanes = 0;
			for (int i = 0; i < segmentInfo.m_lanes.Length; ++i) {
				NetInfo.Direction d = segmentInfo.m_lanes[i].m_finalDirection;
				if ((segmentInfo.m_lanes[i].m_laneType & (NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle)) == NetInfo.LaneType.None || (finalDir != null && d != finalDir))
					continue;

				meanSpeedLimit += segmentInfo.m_lanes[i].m_speedLimit;
				++validLanes;
			}

			if (validLanes > 0)
				meanSpeedLimit /= (float)validLanes;
			ushort ret = LaneToCustomSpeedLimit(meanSpeedLimit);
#if TRACE
			Singleton<CodeProfiler>.instance.Stop("SpeedLimitManager.GetAverageDefaultCustomSpeedLimit");
#endif
			return ret;
		}

        /// <summary>
		/// Determines the average custom speed limit for a given NetInfo object in terms of discrete speed limit levels.
		/// An in-game speed limit of 2.0 (e.g. on highway) is hereby translated into a discrete speed limit value of 100 (km/h).
		/// </summary>
		/// <param name="segmentInfo"></param>
		/// <param name="finalDir"></param>
		/// <returns></returns>
		public ushort GetAverageCustomSpeedLimit(ushort segmentId, ref NetSegment segment, NetInfo segmentInfo, NetInfo.Direction? finalDir = null) {
            // calculate the currently set mean speed limit
            float meanSpeedLimit = 0f;
            uint validLanes = 0;
            uint curLaneId = segment.m_lanes;
            for (uint laneIndex = 0; laneIndex < segmentInfo.m_lanes.Length; ++laneIndex) {
                NetInfo.Lane laneInfo = segmentInfo.m_lanes[laneIndex];
                NetInfo.Direction d = laneInfo.m_finalDirection;
                if ((segmentInfo.m_lanes[laneIndex].m_laneType & (NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle)) == NetInfo.LaneType.None || (finalDir != null && d != finalDir)) {
                    curLaneId = Singleton<NetManager>.instance.m_lanes.m_buffer[curLaneId].m_nextLane;
                    continue;
                }

                meanSpeedLimit += GetLockFreeGameSpeedLimit(segmentId, laneIndex, curLaneId, laneInfo);
                curLaneId = Singleton<NetManager>.instance.m_lanes.m_buffer[curLaneId].m_nextLane;
                ++validLanes;
            }

            if (validLanes > 0)
                meanSpeedLimit /= (float)validLanes;
            return (ushort)Mathf.Round(meanSpeedLimit);
        }

        /// <summary>
        /// Determines the currently set speed limit for the given lane in terms of discrete speed limit levels.
        /// An in-game speed limit of 2.0 (e.g. on highway) is hereby translated into a discrete speed limit value of 100 (km/h).
        /// </summary>
        /// <param name="laneId"></param>
        /// <returns></returns>
        public ushort GetCustomSpeedLimit(uint laneId) {
#if TRACE
			Singleton<CodeProfiler>.instance.Start("SpeedLimitManager.GetCustomSpeedLimit2");
#endif
			// check custom speed limit
			ushort? setSpeedLimit = Flags.getLaneSpeedLimit(laneId);
			if (setSpeedLimit != null) {
#if TRACE
				Singleton<CodeProfiler>.instance.Stop("SpeedLimitManager.GetCustomSpeedLimit2");
#endif
				return (ushort)setSpeedLimit;
			}

			// check default speed limit
			ushort segmentId = Singleton<NetManager>.instance.m_lanes.m_buffer[laneId].m_segment;

			if (segmentId == 0 || (Singleton<NetManager>.instance.m_segments.m_buffer[segmentId].m_flags & NetSegment.Flags.Created) == NetSegment.Flags.None) {
#if TRACE
				Singleton<CodeProfiler>.instance.Stop("SpeedLimitManager.GetCustomSpeedLimit2");
#endif
				return 0;
			}

			var segmentInfo = Singleton<NetManager>.instance.m_segments.m_buffer[segmentId].Info;

			uint curLaneId = Singleton<NetManager>.instance.m_segments.m_buffer[segmentId].m_lanes;
			int laneIndex = 0;
			while (laneIndex < segmentInfo.m_lanes.Length && curLaneId != 0u) {
				if (curLaneId == laneId) {
					ushort ret = LaneToCustomSpeedLimit(segmentInfo.m_lanes[laneIndex].m_speedLimit);
#if TRACE
					Singleton<CodeProfiler>.instance.Stop("SpeedLimitManager.GetCustomSpeedLimit2");
#endif
					return ret;
				}

				laneIndex++;
				curLaneId = Singleton<NetManager>.instance.m_lanes.m_buffer[curLaneId].m_nextLane;
			}

			Log.Warning($"Speed limit for lane {laneId} could not be determined.");
#if TRACE
			Singleton<CodeProfiler>.instance.Stop("SpeedLimitManager.GetCustomSpeedLimit2");
#endif
			return 0; // no speed limit found
		}

		/// <summary>
		/// Determines the currently set speed limit for the given lane in terms of game (floating point) speed limit levels
		/// </summary>
		/// <param name="laneId"></param>
		/// <returns></returns>
		public float GetGameSpeedLimit(uint laneId) {
			return ToGameSpeedLimit(GetCustomSpeedLimit(laneId));
		}

		internal float GetLockFreeGameSpeedLimit(ushort segmentId, uint laneIndex, uint laneId, NetInfo.Lane laneInfo) {
#if TRACE
			Singleton<CodeProfiler>.instance.Start("SpeedLimitManager.GetLockFreeGameSpeedLimit");
#endif
			if (Flags.IsInitDone()) {
				if (Flags.laneSpeedLimitArray.Length <= segmentId) {
					Log.Error($"laneSpeedLimitArray.Length = {Flags.laneSpeedLimitArray.Length}, segmentId={segmentId}. Out of range!");
#if TRACE
					Singleton<CodeProfiler>.instance.Stop("SpeedLimitManager.GetLockFreeGameSpeedLimit");
#endif
					return laneInfo.m_speedLimit;
				}

				float speedLimit = 0;
				ushort?[] fastArray = Flags.laneSpeedLimitArray[segmentId];
				if (fastArray != null && fastArray.Length > laneIndex && fastArray[laneIndex] != null) {
					speedLimit = ToGameSpeedLimit((ushort)fastArray[laneIndex]);
				} else {
					speedLimit = laneInfo.m_speedLimit;
				}
#if TRACE
				Singleton<CodeProfiler>.instance.Stop("SpeedLimitManager.GetLockFreeGameSpeedLimit");
#endif
				return speedLimit;
			} else {
				float ret = GetGameSpeedLimit(laneId);
#if TRACE
				Singleton<CodeProfiler>.instance.Stop("SpeedLimitManager.GetLockFreeGameSpeedLimit");
#endif
				return ret;
			}
		}

		/// <summary>
		/// Converts a custom speed limit to a game speed limit.
		/// </summary>
		/// <param name="customSpeedLimit"></param>
		/// <returns></returns>
		public float ToGameSpeedLimit(ushort customSpeedLimit) {
			if (customSpeedLimit == 0)
				return MAX_SPEED;
			return (float)customSpeedLimit / 50f;
		}

		/// <summary>
		/// Converts a lane speed limit to a custom speed limit.
		/// </summary>
		/// <param name="laneSpeedLimit"></param>
		/// <returns></returns>
		public ushort LaneToCustomSpeedLimit(float laneSpeedLimit, bool roundToSignLimits=true) {
			laneSpeedLimit /= 2f; // 1 == 100 km/h

			if (! roundToSignLimits) {
				return (ushort)Mathf.Round(laneSpeedLimit * 100f);
			}

			// translate the floating point speed limit into our discrete version
			ushort speedLimit = 0;
			if (laneSpeedLimit < 0.15f)
				speedLimit = 10;
			else if (laneSpeedLimit < 1.35f)
				speedLimit = (ushort)((ushort)Mathf.Round(laneSpeedLimit * 10f) * 10u);

			return speedLimit;
		}

		/// <summary>
		/// Explicitly stores currently set speed limits for all segments of the specified NetInfo
		/// </summary>
		/// <param name="info"></param>
		public void FixCurrentSpeedLimits(NetInfo info) {
			if (!customizableNetInfos.Contains(info))
				return;

			for (uint laneId = 1; laneId < NetManager.MAX_LANE_COUNT; ++laneId) {
				if (!NetUtil.IsLaneValid(laneId))
					continue;

				NetInfo laneInfo = Singleton<NetManager>.instance.m_segments.m_buffer[Singleton<NetManager>.instance.m_lanes.m_buffer[laneId].m_segment].Info;
				if (laneInfo.name != info.name && !childNetInfoNamesByCustomizableNetInfoName[info.name].Contains(laneInfo.name))
					continue;

				Flags.setLaneSpeedLimit(laneId, GetCustomSpeedLimit(laneId));
			}
		}

		/// <summary>
		/// Explicitly clear currently set speed limits for all segments of the specified NetInfo
		/// </summary>
		/// <param name="info"></param>
		public void ClearCurrentSpeedLimits(NetInfo info) {
			if (!customizableNetInfos.Contains(info))
				return;

			for (uint laneId = 1; laneId < NetManager.MAX_LANE_COUNT; ++laneId) {
				if (!NetUtil.IsLaneValid(laneId))
					continue;

				NetInfo laneInfo = Singleton<NetManager>.instance.m_segments.m_buffer[Singleton<NetManager>.instance.m_lanes.m_buffer[laneId].m_segment].Info;
				if (laneInfo.name != info.name && childNetInfoNamesByCustomizableNetInfoName.ContainsKey(info.name) && !childNetInfoNamesByCustomizableNetInfoName[info.name].Contains(laneInfo.name))
					continue;

				Flags.removeLaneSpeedLimit(laneId);
			}
		}

		/// <summary>
		/// Determines the game default speed limit of the given NetInfo.
		/// </summary>
		/// <param name="info">the NetInfo of which the game default speed limit should be determined</param>
		/// <param name="roundToSignLimits">if true, custom speed limit are rounded to speed limits available as speed limit sign</param>
		/// <returns></returns>
		public ushort GetVanillaNetInfoSpeedLimit(NetInfo info, bool roundToSignLimits = true) {
			if (info.m_netAI == null)
				return 0;

			if (! (info.m_netAI is RoadBaseAI))
				return 0;

			string infoName = ((RoadBaseAI)info.m_netAI).m_info.name;
			if (! vanillaLaneSpeedLimitsByNetInfoName.ContainsKey(infoName))
				return 0;

			float[] vanillaSpeedLimits = vanillaLaneSpeedLimitsByNetInfoName[infoName];
			float? maxSpeedLimit = null;
			foreach (float speedLimit in vanillaSpeedLimits) {
				if (maxSpeedLimit == null || speedLimit > maxSpeedLimit) {
					maxSpeedLimit = speedLimit;
				}
			}

			if (maxSpeedLimit == null)
				return 0;

			return LaneToCustomSpeedLimit((float)maxSpeedLimit, roundToSignLimits);
		}

		/// <summary>
		/// Determines the custom speed limit of the given NetInfo.
		/// </summary>
		/// <param name="info">the NetInfo of which the custom speed limit should be determined</param>
		/// <returns></returns>
		public int GetCustomNetInfoSpeedLimitIndex(NetInfo info) {
			if (info.m_netAI == null)
				return -1;

			if (!(info.m_netAI is RoadBaseAI))
				return -1;

			string infoName = ((RoadBaseAI)info.m_netAI).m_info.name;
			if (!CustomLaneSpeedLimitIndexByNetInfoName.ContainsKey(infoName))
				return AvailableSpeedLimits.IndexOf(GetVanillaNetInfoSpeedLimit(info, true));

			return CustomLaneSpeedLimitIndexByNetInfoName[infoName];
		}

		/// <summary>
		/// Sets the custom speed limit of the given NetInfo.
		/// </summary>
		/// <param name="info">the NetInfo for which the custom speed limit should be set</param>
		/// <returns></returns>
		public void SetCustomNetInfoSpeedLimitIndex(NetInfo info, int customSpeedLimitIndex) {
			if (info.m_netAI == null)
				return;

			if (!(info.m_netAI is RoadBaseAI))
				return;

			RoadBaseAI baseAI = (RoadBaseAI)info.m_netAI;
			string infoName = baseAI.m_info.name;
			CustomLaneSpeedLimitIndexByNetInfoName[infoName] = customSpeedLimitIndex;

			float gameSpeedLimit = ToGameSpeedLimit(AvailableSpeedLimits[customSpeedLimitIndex]);

			// save speed limit in all NetInfos
			Log._Debug($"Updating parent NetInfo {infoName}: Setting speed limit to {gameSpeedLimit}");
			UpdateNetInfoGameSpeedLimit(baseAI.m_info, gameSpeedLimit);

			if (childNetInfoNamesByCustomizableNetInfoName.ContainsKey(infoName)) {
				foreach (string childNetInfoName in childNetInfoNamesByCustomizableNetInfoName[infoName]) {
					if (NetInfoByName.ContainsKey(childNetInfoName)) {
						Log._Debug($"Updating child NetInfo {childNetInfoName}: Setting speed limit to {gameSpeedLimit}");
						UpdateNetInfoGameSpeedLimit(NetInfoByName[childNetInfoName], gameSpeedLimit);
					}
				}
			}
		}

		private void UpdateNetInfoGameSpeedLimit(NetInfo info, float gameSpeedLimit) {
			if (info == null) {
				Log._Debug($"Updating speed limit of NetInfo: info is null!");
				return;
			}

			Log._Debug($"Updating speed limit of NetInfo {info.name} to {gameSpeedLimit}");

			foreach (NetInfo.Lane lane in info.m_lanes) {
				if ((lane.m_vehicleType & (VehicleInfo.VehicleType.Car | VehicleInfo.VehicleType.Metro | VehicleInfo.VehicleType.Train | VehicleInfo.VehicleType.Tram)) != VehicleInfo.VehicleType.None) {
					lane.m_speedLimit = gameSpeedLimit;
				}
			}
		}

		/// <summary>
		/// Converts a vehicle's velocity to a custom speed.
		/// </summary>
		/// <param name="vehicleSpeed"></param>
		/// <returns></returns>
		public ushort VehicleToCustomSpeed(float vehicleSpeed) {
			return LaneToCustomSpeedLimit(vehicleSpeed / 8f, false);
		}

		/// <summary>
		/// Sets the speed limit of a given segment and lane direction.
		/// </summary>
		/// <param name="segmentId"></param>
		/// <param name="finalDir"></param>
		/// <param name="speedLimit"></param>
		/// <returns></returns>
		public bool SetSpeedLimit(ushort segmentId, NetInfo.Direction finalDir, ushort speedLimit) {
#if TRACE
			Singleton<CodeProfiler>.instance.Start("SpeedLimitManager.SetSpeedLimit");
#endif
			if (segmentId == 0 || !AvailableSpeedLimits.Contains(speedLimit) || (Singleton<NetManager>.instance.m_segments.m_buffer[segmentId].m_flags & NetSegment.Flags.Created) == NetSegment.Flags.None) {
#if TRACE
				Singleton<CodeProfiler>.instance.Stop("SpeedLimitManager.SetSpeedLimit");
#endif
				return false;
			}

			uint curLaneId = Singleton<NetManager>.instance.m_segments.m_buffer[segmentId].m_lanes;
			int laneIndex = 0;
			while (laneIndex < Singleton<NetManager>.instance.m_segments.m_buffer[segmentId].Info.m_lanes.Length && curLaneId != 0u) {
				NetInfo.Direction d = Singleton<NetManager>.instance.m_segments.m_buffer[segmentId].Info.m_lanes[laneIndex].m_finalDirection;
				if ((Singleton<NetManager>.instance.m_segments.m_buffer[segmentId].Info.m_lanes[laneIndex].m_laneType & (NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle)) == NetInfo.LaneType.None || d != finalDir)
					goto nextIter;

#if DEBUG
				Log._Debug($"SpeedLimitManager: Setting speed limit of lane {curLaneId} to {speedLimit}");
#endif
				Flags.setLaneSpeedLimit(curLaneId, speedLimit);

				nextIter:
				curLaneId = Singleton<NetManager>.instance.m_lanes.m_buffer[curLaneId].m_nextLane;
				laneIndex++;
			}

#if TRACE
			Singleton<CodeProfiler>.instance.Stop("SpeedLimitManager.SetSpeedLimit");
#endif
			return true;
		}

		public List<NetInfo> GetCustomizableNetInfos() {
			return customizableNetInfos;
		}

		public void OnBeforeLoadData() {
			// determine vanilla speed limits and customizable NetInfos
			int numLoaded = PrefabCollection<NetInfo>.LoadedCount();
			customizableNetInfos.Clear();
			CustomLaneSpeedLimitIndexByNetInfoName.Clear();

			for (uint i = 0; i < numLoaded; ++i) {
				NetInfo info = PrefabCollection<NetInfo>.GetLoaded(i);
				//Log._Debug($"Iterating over: {info.name}, {info.m_netAI == null}");
				if (info.m_placementStyle == ItemClass.Placement.Manual && info.m_netAI != null && info.m_netAI is RoadBaseAI) {
					string infoName = info.name;
					if (!vanillaLaneSpeedLimitsByNetInfoName.ContainsKey(infoName)) {
						Log._Debug($"Loaded road NetInfo: {infoName}");
						NetInfoByName[infoName] = info;
						customizableNetInfos.Add(info);

						float[] vanillaLaneSpeedLimits = new float[info.m_lanes.Length];
						for (int k = 0; k < info.m_lanes.Length; ++k) {
							vanillaLaneSpeedLimits[k] = info.m_lanes[k].m_speedLimit;
						}
						vanillaLaneSpeedLimitsByNetInfoName[infoName] = vanillaLaneSpeedLimits;
					}
				}
			}

			customizableNetInfos.Sort(delegate(NetInfo a, NetInfo b) {
				RoadBaseAI aAI = (RoadBaseAI)a.m_netAI;
				RoadBaseAI bAI = (RoadBaseAI)b.m_netAI;

				if (aAI.m_highwayRules == bAI.m_highwayRules) {
					int aNumVehicleLanes = 0;
					foreach (NetInfo.Lane lane in a.m_lanes) {
						if ((lane.m_laneType & NetInfo.LaneType.Vehicle) != NetInfo.LaneType.None)
							++aNumVehicleLanes;
					}

					int bNumVehicleLanes = 0;
					foreach (NetInfo.Lane lane in b.m_lanes) {
						if ((lane.m_laneType & NetInfo.LaneType.Vehicle) != NetInfo.LaneType.None)
							++bNumVehicleLanes;
					}

					int res = aNumVehicleLanes.CompareTo(bNumVehicleLanes);
					if (res == 0) {
						return a.name.CompareTo(b.name);
					} else {
						return res;
					}
				} else if (aAI.m_highwayRules) {
					return 1;
				} else {
					return -1;
				}
			});

			// identify parent NetInfos
			for (uint i = 0; i < numLoaded; ++i) {
				NetInfo info = PrefabCollection<NetInfo>.GetLoaded(i);
				if (info.m_placementStyle == ItemClass.Placement.Procedural && info.m_netAI != null && info.m_netAI is RoadBaseAI) {
					string infoName = info.name;
					
					// find parent with prefix name
					foreach (NetInfo parentInfo in customizableNetInfos) {
						if (infoName.StartsWith(parentInfo.name)) {
							Log._Debug($"Identified child NetInfo {infoName} of parent {parentInfo.name}");
							if (! childNetInfoNamesByCustomizableNetInfoName.ContainsKey(parentInfo.name)) {
								childNetInfoNamesByCustomizableNetInfoName[parentInfo.name] = new List<string>();
							}
							childNetInfoNamesByCustomizableNetInfoName[parentInfo.name].Add(info.name);
							NetInfoByName[infoName] = info;
							break;
						}
					}
				}
			}
		}

#if DEBUG
		/*public Dictionary<NetInfo, ushort> GetDefaultSpeedLimits() {
			Dictionary<NetInfo, ushort> ret = new Dictionary<NetInfo, ushort>();
			int numLoaded = PrefabCollection<NetInfo>.LoadedCount();
			for (uint i = 0; i < numLoaded; ++i) {
				NetInfo info = PrefabCollection<NetInfo>.GetLoaded(i);
				ushort defaultSpeedLimit = GetAverageDefaultCustomSpeedLimit(info, NetInfo.Direction.Forward);
				ret.Add(info, defaultSpeedLimit);
				Log._Debug($"Loaded NetInfo: {info.name}, placementStyle={info.m_placementStyle}, availableIn={info.m_availableIn}, thumbnail={info.m_Thumbnail} connectionClass.service: {info.GetConnectionClass().m_service.ToString()}, connectionClass.subService: {info.GetConnectionClass().m_subService.ToString()}, avg. default speed limit: {defaultSpeedLimit}");
			}
			return ret;
		}*/
#endif
	}
}
