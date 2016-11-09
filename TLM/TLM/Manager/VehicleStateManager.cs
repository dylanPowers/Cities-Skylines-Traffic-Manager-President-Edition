﻿#define USEPATHWAITCOUNTERx

using ColossalFramework;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using TrafficManager.Custom.AI;
using TrafficManager.State;
using TrafficManager.Traffic;
using UnityEngine;

namespace TrafficManager.Manager {
	public class VehicleStateManager {
		private static VehicleStateManager instance = null;

		public static VehicleStateManager Instance() {
            if (instance == null)
				instance = new VehicleStateManager();
			return instance;
		}

		/// <summary>
		/// Determines if vehicles should be cleared
		/// </summary>
		private static bool ClearTrafficRequested = false;

		static VehicleStateManager() {
			Instance();
		}

		/// <summary>
		/// Known vehicles and their current known positions. Index: vehicle id
		/// </summary>
		private VehicleState[] VehicleStates = null;

		private VehicleStateManager() {
			VehicleStates = new VehicleState[VehicleManager.MAX_VEHICLE_COUNT];
			for (uint i = 0; i < VehicleManager.MAX_VEHICLE_COUNT; ++i) {
				VehicleStates[i] = new VehicleState((ushort)i);
			}
		}

		/// <summary>
		/// Determines the state of the given vehicle.
		/// </summary>
		/// <param name="vehicleId"></param>
		/// <returns>the vehicle state if the state is valid, null otherwise</returns>
		public VehicleState GetVehicleState(ushort vehicleId) {
#if TRACE
			Singleton<CodeProfiler>.instance.Start("VehicleStateManager.GetVehicleState");
#endif
			VehicleState ret = VehicleStates[vehicleId];
			if (ret.Valid) {
#if TRACE
				Singleton<CodeProfiler>.instance.Stop("VehicleStateManager.GetVehicleState");
#endif
				return ret;
			}
#if TRACE
			Singleton<CodeProfiler>.instance.Stop("VehicleStateManager.GetVehicleState");
#endif
			return null;
		}

		/// <summary>
		/// Determines the state of the given vehicle.
		/// The returned vehicle state is not necessarily valid.
		/// </summary>
		/// <param name="vehicleId"></param>
		/// <returns>the vehicle state</returns>
		internal VehicleState _GetVehicleState(ushort vehicleId) {
#if TRACE
			Singleton<CodeProfiler>.instance.Start("VehicleStateManager._GetVehicleState");
#endif
			VehicleState ret = VehicleStates[vehicleId];
#if TRACE
			Singleton<CodeProfiler>.instance.Stop("VehicleStateManager._GetVehicleState");
#endif
			return ret;
		}

		internal void OnLevelUnloading() {
			for (int i = 0; i < VehicleStates.Length; ++i)
				VehicleStates[i].Valid = false;
		}

		internal void UpdateVehiclePos(ushort vehicleId, ref Vehicle vehicleData) {
#if TRACE
			Singleton<CodeProfiler>.instance.Start("VehicleStateManager.UpdateVehiclePos(1)");
#endif
			VehicleStates[vehicleId].UpdatePosition(ref vehicleData);
#if TRACE
			Singleton<CodeProfiler>.instance.Stop("VehicleStateManager.UpdateVehiclePos(1)");
#endif
		}

		internal void UpdateTrailerPos(ushort trailerId, ref Vehicle trailerData, ushort vehicleId, ref Vehicle vehicleData) {
#if TRACE
			Singleton<CodeProfiler>.instance.Start("VehicleStateManager.UpdateTrailerPos(1)");
#endif
			VehicleStates[trailerId].UpdatePosition(ref trailerData, vehicleId, ref vehicleData);
#if TRACE
			Singleton<CodeProfiler>.instance.Stop("VehicleStateManager.UpdateTrailerPos(1)");
#endif
		}

		internal void UpdateVehiclePos(ushort vehicleId, ref Vehicle vehicleData, ref PathUnit.Position curPos, ref PathUnit.Position nextPos) {
#if TRACE
			Singleton<CodeProfiler>.instance.Start("VehicleStateManager.UpdateVehiclePos(2)");
#endif
			VehicleStates[vehicleId].UpdatePosition(ref vehicleData, ref curPos, ref nextPos);
#if TRACE
			Singleton<CodeProfiler>.instance.Stop("VehicleStateManager.UpdateVehiclePos(2)");
#endif
		}

		internal void LogTraffic(ushort vehicleId, ref Vehicle vehicleData, bool logSpeed) {
#if TRACE
			Singleton<CodeProfiler>.instance.Start("VehicleStateManager.LogTraffic");
#endif
			VehicleState state = GetVehicleState(vehicleId);
			if (state == null)
				return;

			ushort length = (ushort)state.TotalLength;
			if (length == 0)
				return;
			ushort? speed = logSpeed ? (ushort?)Mathf.RoundToInt(vehicleData.GetLastFrameData().m_velocity.magnitude) : null;

			state.ProcessCurrentPathPosition(ref vehicleData, delegate (ref PathUnit.Position pos) {
				CustomRoadAI.AddTraffic(pos.m_segment, Singleton<NetManager>.instance.m_segments.m_buffer[pos.m_segment].Info.m_lanes[pos.m_lane].m_finalDirection, length, speed);
			});
#if TRACE
			Singleton<CodeProfiler>.instance.Stop("VehicleStateManager.LogTraffic");
#endif
		}

		internal void OnReleaseVehicle(ushort vehicleId) {
#if TRACE
			Singleton<CodeProfiler>.instance.Start("VehicleStateManager.OnReleaseVehicle");
#endif
#if DEBUG
			//Log._Debug($"VehicleStateManager.OnReleaseVehicle({vehicleId}) called.");
#endif
			VehicleState state = _GetVehicleState(vehicleId);
			state.VehicleType = ExtVehicleType.None;
			ExtCitizenInstance driverExtInstance = state.GetDriverExtInstance();
			if (driverExtInstance != null) {
				//driverExtInstance.FailedParkingAttempts = 0;
				driverExtInstance.Reset();
				state.DriverInstanceId = 0;
			}
#if USEPATHWAITCOUNTER
			state.PathWaitCounter = 0;
#endif
			state.Valid = false;
			//VehicleStates[vehicleId].Reset();
#if TRACE
			Singleton<CodeProfiler>.instance.Stop("VehicleStateManager.OnReleaseVehicle");
#endif
		}

		internal void OnVehicleSpawned(ushort vehicleId, ref Vehicle vehicleData) {
			//Log._Debug($"VehicleStateManager: OnPathFindReady({vehicleId})");
#if TRACE
			Singleton<CodeProfiler>.instance.Start("VehicleStateManager.OnPathFindReady");
#endif
			VehicleStates[vehicleId].OnVehicleSpawned(ref vehicleData);
#if TRACE
			Singleton<CodeProfiler>.instance.Stop("VehicleStateManager.OnPathFindReady");
#endif
		}

		internal void InitAllVehicles() {
			Log._Debug("VehicleStateManager: InitAllVehicles()");
			if (Options.prioritySignsEnabled || Options.timedLightsEnabled) {
				VehicleManager vehicleManager = Singleton<VehicleManager>.instance;

				for (uint vehicleId = 0; vehicleId < VehicleManager.MAX_VEHICLE_COUNT; ++vehicleId) {
					if ((Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleId].m_flags & Vehicle.Flags.Created) == 0)
						continue;

					try {
						DetermineVehicleType((ushort)vehicleId, ref Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleId]);
						OnVehicleSpawned((ushort)vehicleId, ref Singleton<VehicleManager>.instance.m_vehicles.m_buffer[vehicleId]);
					} catch (Exception e) {
						Log.Error("VehicleStateManager: InitAllVehicles Error: " + e.ToString());
					}
				}
			}
		}

		internal ExtVehicleType? DetermineVehicleType(ushort vehicleId, ref Vehicle vehicleData) {
#if TRACE
			Singleton<CodeProfiler>.instance.Start("VehicleStateManager.DetermineVehicleType");
#endif
			if ((vehicleData.m_flags & Vehicle.Flags.Emergency2) != 0) {
#if TRACE
			Singleton<CodeProfiler>.instance.Stop("VehicleStateManager.DetermineVehicleType");
#endif
				VehicleStates[vehicleId].VehicleType = ExtVehicleType.Emergency;
				return ExtVehicleType.Emergency;
			}

			VehicleAI ai = vehicleData.Info.m_vehicleAI;
#if TRACE
			Singleton<CodeProfiler>.instance.Start("VehicleStateManager.DetermineVehicleTypeFromAIType");
#endif
			ExtVehicleType? ret = DetermineVehicleTypeFromAIType(ai, false);
#if TRACE
			Singleton<CodeProfiler>.instance.Stop("VehicleStateManager.DetermineVehicleTypeFromAIType");
#endif

			if (ret != null) {
				VehicleStates[vehicleId].VehicleType = (ExtVehicleType)ret;
				if ((ExtVehicleType)ret == ExtVehicleType.CargoTruck) {
					VehicleStates[vehicleId].HeavyVehicle = ((CargoTruckAI)ai).m_isHeavyVehicle;
				}
			} else {
				VehicleStates[vehicleId].VehicleType = ExtVehicleType.None;
				VehicleStates[vehicleId].HeavyVehicle = false;
			}
#if TRACE
			Singleton<CodeProfiler>.instance.Stop("VehicleStateManager.DetermineVehicleType");
#endif
			return ret;
		}

		private ExtVehicleType? DetermineVehicleTypeFromAIType(VehicleAI ai, bool emergencyOnDuty) {
			if (emergencyOnDuty)
				return ExtVehicleType.Emergency;

			switch (ai.m_info.m_vehicleType) {
				case VehicleInfo.VehicleType.Bicycle:
					return ExtVehicleType.Bicycle;
				case VehicleInfo.VehicleType.Car:
					if (ai is PassengerCarAI)
						return ExtVehicleType.PassengerCar;
					if (ai is AmbulanceAI || ai is FireTruckAI || ai is PoliceCarAI || ai is HearseAI || ai is GarbageTruckAI || ai is MaintenanceTruckAI || ai is SnowTruckAI) {
						return ExtVehicleType.Service;
					}
					if (ai is CarTrailerAI)
						return ExtVehicleType.None;
					if (ai is BusAI)
						return ExtVehicleType.Bus;
					if (ai is TaxiAI)
						return ExtVehicleType.Taxi;
					if (ai is CargoTruckAI)
						return ExtVehicleType.CargoTruck;
					break;
				case VehicleInfo.VehicleType.Metro:
				case VehicleInfo.VehicleType.Train:
					if (ai is PassengerTrainAI)
						return ExtVehicleType.PassengerTrain;
					//if (ai is CargoTrainAI)
					return ExtVehicleType.CargoTrain;
				//break;
				case VehicleInfo.VehicleType.Tram:
					return ExtVehicleType.Tram;
				case VehicleInfo.VehicleType.Ship:
					if (ai is PassengerShipAI)
						return ExtVehicleType.PassengerShip;
					//if (ai is CargoShipAI)
					return ExtVehicleType.CargoShip;
				//break;
				case VehicleInfo.VehicleType.Plane:
					//if (ai is PassengerPlaneAI)
					return ExtVehicleType.PassengerPlane;
					//break;
			}
#if DEBUGVSTATE
			Log._Debug($"Could not determine vehicle type from ai type: {ai.GetType().ToString()}");
#endif
			return null;
		}

		internal void SimulationStep() {
			try {
				if (ClearTrafficRequested) {
					ClearTraffic();
					ClearTrafficRequested = false;
				}
			} finally { }
		}

		internal void ClearTraffic() {
			try {
				Monitor.Enter(Singleton<VehicleManager>.instance);

				for (ushort i = 0; i < Singleton<VehicleManager>.instance.m_vehicles.m_size; ++i) {
					if (
						(Singleton<VehicleManager>.instance.m_vehicles.m_buffer[i].m_flags & Vehicle.Flags.Created) != 0)
						Singleton<VehicleManager>.instance.ReleaseVehicle(i);
				}

				CustomRoadAI.resetTrafficStats();
			} catch (Exception ex) {
				Log.Error($"Error occured while trying to clear traffic: {ex.ToString()}");
			} finally {
				Monitor.Exit(Singleton<VehicleManager>.instance);
			}
		}

		internal void RequestClearTraffic() {
			ClearTrafficRequested = true;
		}
	}
}
