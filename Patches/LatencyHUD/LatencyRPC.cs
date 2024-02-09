/**
*	This source code is part of LCDirectLAN project,
*	LCDirectLAN is a mod for Lethal Company that is built around BepInEx to fix and enhances LAN lobbies.
*
*	Project Repository:
*		https://github.com/TIRTAGT/LCDirectLAN
*
*	This project is open source and are released under the MIT License,
*	for more information, please read the LICENSE file in the project repository.
*
*	Copyright (c) 2024 Matthew Tirtawidjaja <matthew@tirtagt.xyz>
**/

using GameNetcodeStuff;
using HarmonyLib;
using System;
using System.Collections;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace LCDirectLAN.Patches.LatencyHUD
{
	internal class LatencyRPC : NetworkBehaviour
	{
		private static bool IsWaitingPingCallback = false;
		private static Coroutine LatencyTrackerCoroutine = null;
		private static bool AlreadySentServerLagWarning = false;

		[HarmonyPatch(typeof(PlayerControllerB), "ConnectClientToPlayerObject")]
		[HarmonyPostfix]
		[HarmonyPriority(Priority.VeryLow)]
		public static void Postfix_ConnectClientToPlayerObject(PlayerControllerB __instance)
		{
			if (!LCDirectLan.IsOnLanMode) { return; }

			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, "LatencyRPC.Postfix_ConnectClientToPlayerObject()");

			if (__instance.NetworkManager == null || !__instance.NetworkManager.IsListening) { return; }

			// If this is player is not controlled by us
			if (!__instance.IsOwner) { return; }

			// Listen for server side ping callback
			NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(LCDirectLan.PLUGIN_NAME + "_LatencyRPC_ToClientRpc", new CustomMessagingManager.HandleNamedMessageDelegate(LatencyRPC_ToClientRpc));
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"Listening _LatencyRPC_ToClientRpc");

			// Do not allow clients to handle server events
			if (!__instance.NetworkManager.IsServer) {
				// As a client, start tracking latency here
				StartTrackingLatency(__instance);
				return;
			}

			// Listen for client side ping request
			NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(LCDirectLan.PLUGIN_NAME + "_LatencyRPC_ToServerRpc", new CustomMessagingManager.HandleNamedMessageDelegate(LatencyRPC_ToServerRpc));
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"Listening _LatencyRPC_ToServerRpc()");

			// As a server, start tracking latency here
			StartTrackingLatency(__instance);
		}

		[HarmonyPatch(typeof(PlayerControllerB), "OnDestroy")]
		[HarmonyPrefix]
		[HarmonyPriority(Priority.VeryLow)]
		public static void Prefix_OnDestroy(PlayerControllerB __instance)
		{
			if (!LCDirectLan.IsOnLanMode) { return; }

			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, "LatencyRPC.Prefix_OnDestroy()");

			// Stop tracking latency
			StopTrackingLatency(__instance);
		}

		/// <summary>
		/// Start tracking latency
		/// </summary>
		/// <param name="__instance">A MonoBehaviour instance in order to be able to start coroutines</param>
		public static void StartTrackingLatency(MonoBehaviour __instance)
		{
			if (!LCDirectLan.IsOnLanMode) { return; }

			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, "LatencyRPC.StartTrackingLatency()");

			// Avoid starting multiple coroutines
			if (LatencyTrackerCoroutine != null) {
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, "LatencyTrackerCoroutine is already running !");
				return;
			}

			// Start the latency tracking coroutine
			LatencyTrackerCoroutine = __instance.StartCoroutine(LatencyTracker_Coroutine());
		}

		/// <summary>
		/// The latency tracking coroutine
		/// </summary>
		/// <returns>An IEnumerator for the coroutine</returns>
		private static IEnumerator LatencyTracker_Coroutine()
		{
			if (!LCDirectLan.IsOnLanMode) { yield break; }

			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, "LatencyRPC.StartTrackingLatency()");

			while (true)
			{
				// Send a ping request to the server
				FastBufferWriter writer = new FastBufferWriter(8, Allocator.Temp);
				ulong CurrentTimestamp = (ulong)(DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond);
				writer.WriteValue(CurrentTimestamp);

				IsWaitingPingCallback = true;
				NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(LCDirectLan.PLUGIN_NAME + "_LatencyRPC_ToServerRpc", 0, writer, NetworkDelivery.Reliable);

				yield return new WaitUntil(() => !IsWaitingPingCallback);
				yield return new WaitForSeconds(1.5F);
			}
		}

		/// <summary>
		/// Stop tracking latency
		/// </summary>
		/// <param name="__instance">A MonoBehaviour instance in order to be able to stop coroutines</param>
		public static void StopTrackingLatency(MonoBehaviour __instance)
		{
			if (!LCDirectLan.IsOnLanMode) { return; }

			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, "LatencyRPC.StopTrackingLatency()");

			// Stop the latency tracking coroutine
			if (LatencyTrackerCoroutine != null) {
				__instance.StopCoroutine(LatencyTrackerCoroutine);
				LatencyTrackerCoroutine = null;
			}
		}

		[ServerRpc]
		public static void LatencyRPC_ToServerRpc(ulong ClientID, FastBufferReader reader)
		{
			// Capture the request received as soon as possible
			ulong RequestReceivedTimestamp = (ulong)(DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond);

			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"_LatencyRPC_ToServerRpc({ClientID})");

			reader.ReadValue(out ulong ClientPingTimestamp);

			// Get actual Player ID
			int PlayerID = Utility.allPlayerScripts.GetActualPlayerIndex(ClientID);

			// Calculate the RTT latency from the server in ms
			ushort ClientLatency = (ushort)(RequestReceivedTimestamp - ClientPingTimestamp);

			// Send the latency back to the client
			FastBufferWriter writer = new FastBufferWriter(24, Allocator.Temp);

			// Write the client's first ping timestamp
			writer.WriteValue(ClientPingTimestamp);

			// Write when the server received the request
			writer.WriteValue(RequestReceivedTimestamp);

			// Write when the server is about to send the callback (Server Processing Timestamp)
			ulong AfterServerProcessingTimestamp = (ulong)(DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond);
			writer.WriteValue(AfterServerProcessingTimestamp);

			// Send callback to the client
			NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(LCDirectLan.PLUGIN_NAME + "_LatencyRPC_ToClientRpc", ClientID, writer, NetworkDelivery.Reliable);

			// Count how much processing time the server took to send the callback
			ushort ServerProcessingLag = (ushort)(AfterServerProcessingTimestamp - RequestReceivedTimestamp);

			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"Player #{PlayerID} ({Utility.allPlayerScripts.GetPlayerUsername(PlayerID)})'s RTT Latency: {ClientLatency}ms, Server Processing Lag: {ServerProcessingLag}ms");
		}

		/// <summary>
		/// Event handler for Server side ping callback
		/// </summary>
		/// <param name="clientId">The request source (always the server, which is 0)</param>
		/// <param name="reader">The buffer data</param>
		[ClientRpc]
		public static void LatencyRPC_ToClientRpc(ulong ClientID, FastBufferReader reader)
		{
			/** This event is called from server to client when the server receives a ping request from the client
			 * 
			 *  Payload:
			 *      byte 1 - 8: Timestamp from the first client ping request, proxied by the server
			 *  	byte 9 - 16: Timestamp from server side when the request is recognized
			 *  	byte 17 - 24: Timestamp from server side when the callback is near to be sent
			 **/

			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"_LatencyRPC_ToClientRpc({ClientID})");

			ulong CurrentTimestamp = (ulong)(DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond);
			reader.ReadValue(out ulong ClientPingTimestamp);
			reader.ReadValue(out ulong PingRequestReceivedTimestamp);
			reader.ReadValue(out ulong AfterServerProcessingTimestamp);

			ushort latency;
			
			// Check if we should measure RTT instead of one-way latency
			if (LCDirectLan.GetConfig<bool>("Latency HUD", "RTTMeasurement")) {
				latency = (ushort)(CurrentTimestamp - ClientPingTimestamp);
			}
			else {
				latency = (ushort)(PingRequestReceivedTimestamp - ClientPingTimestamp);
			}

			// Calculate the server processing lag in ms
			ushort ServerProcessingLag = (ushort)(AfterServerProcessingTimestamp - PingRequestReceivedTimestamp);

			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"Our RTT Latency: {latency}ms, Server Processing Lag: {ServerProcessingLag}ms");

			// Update the HUD
			LatencyHUD.HUDManagerPatch.UpdateLatencyHUD(latency);

			// If server processing lag is quite high, notify the client
			if (ServerProcessingLag > 500) {
				if (AlreadySentServerLagWarning) { return; }

				AlreadySentServerLagWarning = true;
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, $"Server Processing Lag is too high: {ServerProcessingLag}ms");
				LatencyHUD.HUDManagerPatch.SendServerLagWarning(ServerProcessingLag);
			}
			else {
				AlreadySentServerLagWarning = false;
			}

			IsWaitingPingCallback = false;
		}
	}
}
