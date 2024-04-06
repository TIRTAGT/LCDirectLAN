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
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace LCDirectLAN.Patches.LatencyHUD
{
	internal class LatencyRPC : NetworkBehaviour
	{
		private static bool IsWaitingPingCallback = false;
		private static Coroutine LatencyTrackerCoroutine;
		private static bool UseCustomLatencyRPC = true;
		private static UnityTransport UnityTransportObject;

		[HarmonyPatch(typeof(PlayerControllerB), "ConnectClientToPlayerObject")]
		[HarmonyPostfix]
		[HarmonyPriority(Priority.VeryLow)]
		public static void Postfix_ConnectClientToPlayerObject(PlayerControllerB __instance)
		{
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, "LatencyRPC.Postfix_ConnectClientToPlayerObject()");

			if (__instance.NetworkManager == null || !__instance.NetworkManager.IsListening) { return; }

			// Only manage player object that is controlled by us
			if (!__instance.IsOwner) { return; }

			// Check if we should use Custom Latency RPC or UnityTransport's RTT
			UseCustomLatencyRPC = !LCDirectLan.GetConfig<bool>("Latency HUD", "DisableCustomLatencyRPC");

			if (UseCustomLatencyRPC) {
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, "Using Custom Latency RPC");
			}
			else {
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, "Using UnityTransport's RTT");

				// Get the UnityTransport object
				UnityTransportObject = GameObject.Find("NetworkManager").GetComponent<UnityTransport>();

				// Start tracking latency here, no need to listen using Custom Latency RPC
				StartTrackingLatency(__instance);
				return;
			}

			// Listen callback from server for our (client) ping request
			NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(LCDirectLan.PLUGIN_NAME + "_ClientLatencyRequestCallback_ToClientRpc", new CustomMessagingManager.HandleNamedMessageDelegate(ClientLatencyRequestCallback));
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"Listening _ClientLatencyRequestCallback_ToClientRpc");

			// Do not allow clients to handle server events
			if (!__instance.NetworkManager.IsServer) {
				// As a client, start tracking latency here
				StartTrackingLatency(__instance);
				return;
			}

			// Listen ping request from client
			NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(LCDirectLan.PLUGIN_NAME + "_ClientLatencyRequest_ToServerRpc", new CustomMessagingManager.HandleNamedMessageDelegate(ClientLatencyRequest));
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"Listening _ClientLatencyRequest_ToServerRpc()");

			// Listen callback from client to give back our (server) ping request
			NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(LCDirectLan.PLUGIN_NAME + "_ServerLatencyRequestCallback_ToServerRpc", new CustomMessagingManager.HandleNamedMessageDelegate(ServerLatencyRequestCallback));
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"Listening _ServerLatencyRequestCallback_ToServerRpc()");

			// Check if we shouldn't track latency to ourself
			if (LCDirectLan.GetConfig<bool>("Latency HUD", "HideHUDWhileHosting") && NetworkManager.Singleton.IsServer) {
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, "Not tracking latency as a server !");
				return;
			}

			// As a server, start tracking latency here
			StartTrackingLatency(__instance);
		}

		[HarmonyPatch(typeof(PlayerControllerB), "OnDestroy")]
		[HarmonyPrefix]
		[HarmonyPriority(Priority.VeryLow)]
		public static void Prefix_OnDestroy(PlayerControllerB __instance)
		{
			// Only manage player object that is controlled by us
			if (!__instance.IsOwner) { return; }

			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, "LatencyRPC.Prefix_OnDestroy()");

			// Stop tracking latency
			StopTrackingLatency(__instance);
			UnityTransportObject = null;

			if (__instance.NetworkManager == null || !__instance.NetworkManager.IsListening) { return; }
			if (NetworkManager.Singleton == null) { return; }

			// Unregister message handlers
			NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(LCDirectLan.PLUGIN_NAME + "_ClientLatencyRequestCallback_ToClientRpc");

			// Do not allow clients to handle server events
			if (__instance.NetworkManager.IsServer) {
				NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(LCDirectLan.PLUGIN_NAME + "_ClientLatencyRequest_ToServerRpc");
				NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(LCDirectLan.PLUGIN_NAME + "_ServerLatencyRequestCallback_ToServerRpc");
			}
		}

		/// <summary>
		/// Start tracking latency
		/// </summary>
		/// <param name="__instance">A MonoBehaviour instance in order to be able to start coroutines</param>
		public static void StartTrackingLatency(MonoBehaviour __instance)
		{
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

			float SecondsSinceIsWaitingPing = 0;
			float TargetPollingInterval = 3; // in seconds
			float ServerWarnTimeout = 4.5F; // in seconds
			bool RetryPing = false;
			bool HasSentHUDWarning = false;
			bool KeepRunning = true;
			bool IsServerSupportLatencyRPC = false;

			while (KeepRunning)
			{
				if (!IsWaitingPingCallback || RetryPing) {
					if (!UseCustomLatencyRPC) {
						IsWaitingPingCallback = false;

						if (!FetchUnityLatency(ref SecondsSinceIsWaitingPing)) {
							HUDManagerPatch.DestroyLatencyHUD();
							yield break;
						}
					}
					else {
						if (NetworkManager.Singleton == null) {
							LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, "NetworkManager.Singleton is null, cannot send LatencyRPC ping request !");
							
							HUDManagerPatch.DisplayHUDWarning("Unable to find NetworkManager for LatencyRPC ping request");
							
							// Get the UnityTransport object
							UnityTransportObject = GameObject.Find("NetworkManager").GetComponent<UnityTransport>();

							// Try again without Custom Latency RPC
							UseCustomLatencyRPC = false;
							RetryPing = true;
							continue;
						}

						SendCustomLatencyRequest(ref SecondsSinceIsWaitingPing, ref IsWaitingPingCallback);
					}
				}

				yield return new WaitForSeconds(0.2F);
				SecondsSinceIsWaitingPing += 0.2F;

				// If we aren't waiting for a callback
				if (!IsWaitingPingCallback) {
					if (UseCustomLatencyRPC) { IsServerSupportLatencyRPC = true; }
					RetryPing = false;
					HasSentHUDWarning = false;
					
					// Check if we should sleep until the next polling interval
					if (SecondsSinceIsWaitingPing < TargetPollingInterval) {
						// Sleep until the next polling interval
						yield return new WaitForSeconds(TargetPollingInterval - SecondsSinceIsWaitingPing);
					}

					// Continue to the next polling interval
					continue;
				}

				// Check if we have waited for too long
				if (SecondsSinceIsWaitingPing >= ServerWarnTimeout)
				{
					// If Server haven't responded to any of our ping request, they may not support LatencyRPC
					if (UseCustomLatencyRPC && !IsServerSupportLatencyRPC) {
						LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, "Server did not respond to first ping request, server may not support LatencyRPC !");
						HUDManagerPatch.DisplayHUDWarning("Server did not support LatencyRPC, switching to UnityTransport...");

						// Get the UnityTransport object
						UnityTransportObject = GameObject.Find("NetworkManager").GetComponent<UnityTransport>();

						// Switch to use UnityTransport's RTT
						UseCustomLatencyRPC = false;

						// Retry the ping request
						RetryPing = true;

						// Continue to the next polling interval
						continue;
					}

					LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, "Server have not responded to our last ping request !");
					HUDManagerPatch.UpdateLatencyHUD((ushort)(SecondsSinceIsWaitingPing * 1000));
					
					if (!HasSentHUDWarning) {
						// Send a warning to the HUD
						HUDManagerPatch.DisplayHUDWarning("Server stopped responding to ping, slow server or connection lost ?");
						HasSentHUDWarning = true;
					}

					// Throttle retrying the ping request
					yield return new WaitForSeconds(1.5F);
					SecondsSinceIsWaitingPing += 1.5F;

					// Retry the ping request
					RetryPing = true;
				}
			}
		}

		/// <summary>
		/// Fetch the latency using UnityTransport's RTT
		/// </summary>
		/// <param name="SecondsSinceIsWaitingPing">Seconds since the last time we are waiting for a ping callback</param>
		/// <returns>Boolean representing whether the latency is successfully fetched</returns>
		private static bool FetchUnityLatency(ref float SecondsSinceIsWaitingPing) {
			if (UnityTransportObject == null) {
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, "UnityTransportObject is null, Latency Patch will be disabled !");
				HUDManagerPatch.DisplayHUDWarning("Unable to find UnityTransport, Latency Patch will be disabled.");

				return false;
			}
			
			SecondsSinceIsWaitingPing = 0;

			ulong latency = UnityTransportObject.GetCurrentRtt(0);

			// Check if we should measure RTT instead of one-way latency
			if (LCDirectLan.GetConfig<bool>("Latency HUD", "RTTMeasurement")) {
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"Our RTT Latency (Unity): {latency}ms");
			}
			else {
				latency /= 2;
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"Our One-Way Latency (Unity): {latency}ms");
			}

			// Update the HUD
			HUDManagerPatch.UpdateLatencyHUD((ushort)latency);

			return true;
		}

		/// <summary>
		/// Send a ping request to the server (using LCDirectLAN's Latency RPC, requires server support)
		/// </summary>
		/// <param name="SecondsSinceIsWaitingPing">Seconds since the last time we are waiting for a ping callback</param>
		/// <param name="IsWaitingPingCallback">Whether we are waiting for a ping callback</param>
		private static void SendCustomLatencyRequest(ref float SecondsSinceIsWaitingPing, ref bool IsWaitingPingCallback) {
			// Send a ping request to the server
			FastBufferWriter writer = new FastBufferWriter(8, Allocator.Temp);
			writer.WriteValue(GetCurrentEpochMilis());

			if (!IsWaitingPingCallback) {
				IsWaitingPingCallback = true;
				SecondsSinceIsWaitingPing = 0;
			}

			NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(LCDirectLan.PLUGIN_NAME + "_ClientLatencyRequest_ToServerRpc", 0, writer, NetworkDelivery.Unreliable);
		}

		/// <summary>
		/// Stop tracking latency
		/// </summary>
		/// <param name="__instance">A MonoBehaviour instance in order to be able to stop coroutines</param>
		public static void StopTrackingLatency(MonoBehaviour __instance)
		{
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, "LatencyRPC.StopTrackingLatency()");

			// Stop the latency tracking coroutine
			if (LatencyTrackerCoroutine != null) {
				if (__instance == null) {
					LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, "Cannot stop LatencyRPC.LatencyTrackerCoroutine, MonoBehaviour instance is null !");
					LatencyTrackerCoroutine = null;
					return;
				}

				__instance.StopCoroutine(LatencyTrackerCoroutine);
				LatencyTrackerCoroutine = null;
			}
		}

		[ServerRpc]
		public static void ClientLatencyRequest(ulong ClientID, FastBufferReader reader)
		{
			/** ClientLatencyRequest to Server Event Payload:
			 *      byte 1-8: Client-side timestamp when the request is sent
			 **/
			reader.ReadValue(out long ClientRequestTimestamp);

			FastBufferWriter writer = new FastBufferWriter(16, Allocator.Temp);
			
			// Write back the client's timestamp
			writer.WriteValue(ClientRequestTimestamp);

			// Include our timestamp back to the client so we can calculate the latency using our timing
			writer.WriteValue(GetCurrentEpochMilis());

			// Send callback to the client
			NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(LCDirectLan.PLUGIN_NAME + "_ClientLatencyRequestCallback_ToClientRpc", ClientID, writer, NetworkDelivery.Unreliable);

			// No need to expose the data for the host (Player #0) since the server is also a client
			if (ClientID == 0) { return; }

			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"Replied to ClientID {ClientID}'s ping request !");
		}

		/// <summary>
		/// Event handler for Server side ping callback
		/// </summary>
		/// <param name="clientId">The request source (always the server, which is 0)</param>
		/// <param name="reader">The buffer data</param>
		[ClientRpc]
		public static void ClientLatencyRequestCallback(ulong ClientID, FastBufferReader reader)
		{
			/** ClientLatencyRequestCallback to Client Event Payload:
			 *      byte 1-8: Client-side timestamp when the request is sent
			 *      byte 9-16: Server-side timestamp when the request is received
			 **/
			long ClientReceivedTimestamp = GetCurrentEpochMilis();

			reader.ReadValue(out long ClientRequestTimestamp);
			reader.ReadValue(out long ServerRequestTimestamp);

			FastBufferWriter writer = new FastBufferWriter(8, Allocator.Temp);

			// Send the server's timestamp back to the server so they can calculate the latency using their own timing
			writer.WriteValue(ServerRequestTimestamp);

			// Send callback to the server
			NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(LCDirectLan.PLUGIN_NAME + "_ServerLatencyRequestCallback_ToServerRpc", 0, writer, NetworkDelivery.Unreliable);

			ushort latency = (ushort)(ClientReceivedTimestamp - ClientRequestTimestamp);

			// Check if we should measure RTT instead of one-way latency
			if (LCDirectLan.GetConfig<bool>("Latency HUD", "RTTMeasurement")) {
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"Our RTT Latency: {latency}ms");
			}
			else {
				latency /= 2;
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"Our One-Way Latency: {latency}ms");
			}

			// Update the HUD
			HUDManagerPatch.UpdateLatencyHUD(latency);

			IsWaitingPingCallback = false;
		}

		/// <summary>
		/// Event handler for Server ping request callback
		/// </summary>
		/// <param name="ClientID">The callback source (the client)</param>
		/// <param name="reader">The buffer data</param>
		[ServerRpc]
		public static void ServerLatencyRequestCallback(ulong ClientID, FastBufferReader reader)
		{
			/** ClientLatencyRequestCallback to Client Event Payload:
			 *      byte 1-8: Server-side timestamp when the request is received
			 **/
			long ServerReceivedTimestamp = GetCurrentEpochMilis();

			reader.ReadValue(out long ServerRequestTimestamp);

			// Get actual Player ID
			int PlayerID = Utility.allPlayerScripts.GetActualPlayerIndex(ClientID);

			// No need to show latency data for our self (server/host/Player #0)
			if (PlayerID == 0) { return; }

			short latency = (short)(ServerReceivedTimestamp - ServerRequestTimestamp);

			string PlayerUsername = Utility.allPlayerScripts.GetPlayerUsername(PlayerID);

			// If player does not have custom username, log as Player #ID
			if (PlayerUsername == $"Player #{PlayerID}") {
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"Player #{PlayerID}'s RTT Latency: {latency}ms, Estimated One-Way Latency: {latency / 2}ms");
				return;
			}

			// Player has custom username, log as their username
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"{PlayerUsername}'s RTT Latency: {latency}ms, Estimated One-Way Latency: {latency / 2}ms");
		}

		/// <summary>
		/// Get the current epoch time in milliseconds
		/// </summary>
		/// <returns>The current epoch time in milliseconds</returns>
		private static long GetCurrentEpochMilis()
		{
			return DateTimeOffset.Now.ToUnixTimeMilliseconds();
		}
	}
}
