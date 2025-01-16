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
using LCDirectLAN.Utility;
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
		private static Coroutine LatencyTrackerCoroutine;
		private static bool UseCustomLatencyRPC = true;
		private static UnityTransport UnityTransportObject;
		private static ushort[] PlayersLatency = new ushort[0];
		private static long[] PlayersLastPingToServer = new long[0];
		private static long ServerLastPingToMeTimestamp = 0;
		private static readonly float CLIENT_PING_INTERVAL_SECONDS = 3;
		private static readonly float SERVER_GLOBAL_LATENCY_REFRESH_INTERVAL_SECONDS = CLIENT_PING_INTERVAL_SECONDS / 2;
		private static readonly float SLOW_SERVER_THRESHOLD_SECONDS = 15;

		[HarmonyPatch(typeof(PlayerControllerB), "ConnectClientToPlayerObject")]
		[HarmonyPostfix]
		[HarmonyPriority(Priority.VeryLow)]
		public static void Postfix_ConnectClientToPlayerObject(PlayerControllerB __instance)
		{
			if (__instance.NetworkManager == null || !__instance.NetworkManager.IsListening) { return; }

			// Only manage player object that is controlled by us
			if (!__instance.IsOwner) { return; }

			if (StartOfRound.Instance == null)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, "StartOfRound.Instance is null, cannot create array for PlayersLatency !");
				return;
			}

			PlayersLatency = new ushort[StartOfRound.Instance.allPlayerScripts.Length];

			// Check if we should use Custom Latency RPC or UnityTransport's RTT
			UseCustomLatencyRPC = !LCDirectLan.GetConfig<bool>("Latency HUD", "DisableCustomLatencyRPC");

			if (!UseCustomLatencyRPC)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, "Using UnityTransport's RTT");

				// Get the UnityTransport object
				UnityTransportObject = GameObject.Find("NetworkManager").GetComponent<UnityTransport>();

				// Start tracking latency here, no need to listen using Custom Latency RPC
				StartTrackingLatency(__instance);
				return;
			}
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, "Using Custom Latency RPC");

			// Listen callback from server for global latency refresh
			NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(LCDirectLan.PLUGIN_NAME + "_GlobalLatencyRefresh_ToClientRpc", new CustomMessagingManager.HandleNamedMessageDelegate(OnServer_GlobalLatencyRefresh));
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"Listening {LCDirectLan.PLUGIN_NAME}_GlobalLatencyRefresh_ToClientRpc");

			// Do not allow clients to handle server events
			if (__instance.NetworkManager.IsServer)
			{
				PlayersLastPingToServer = new long[StartOfRound.Instance.allPlayerScripts.Length];

				// Listen ping request from client
				NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(LCDirectLan.PLUGIN_NAME + "_OnClientPingRequest_ToServerRpc", new CustomMessagingManager.HandleNamedMessageDelegate(OnClient_PingRequest));
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"Listening {LCDirectLan.PLUGIN_NAME}_OnClientPingRequest_ToServerRpc()");
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

			// Stop tracking latency
			StopTrackingLatency(__instance);
			UnityTransportObject = null;

			if (__instance.NetworkManager == null || !__instance.NetworkManager.IsListening) { return; }
			if (NetworkManager.Singleton == null) { return; }

			// Unregister message handlers
			NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(LCDirectLan.PLUGIN_NAME + "_GlobalLatencyRefresh_ToClientRpc");

			// Do not allow clients to handle server events
			if (__instance.NetworkManager.IsServer)
			{
				NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(LCDirectLan.PLUGIN_NAME + "_OnClientPingRequest");
			}
		}

		/// <summary>
		/// Start coroutines to track and broadcast players latency (if server) or send periodic ping report (if client)
		/// </summary>
		/// <param name="__instance">A MonoBehaviour instance in order to be able to start coroutines</param>
		public static void StartTrackingLatency(MonoBehaviour __instance)
		{
			// Avoid starting multiple coroutines
			if (LatencyTrackerCoroutine != null)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, "LatencyRPC.LatencyTrackerCoroutine is already running !");
				return;
			}

			if (NetworkManager.Singleton == null)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, "NetworkManager.Singleton is null, cannot start LatencyRPC !");
				return;
			}

			// Start the latency tracking coroutine
			if (NetworkManager.Singleton.IsServer)
			{
				LatencyTrackerCoroutine = __instance.StartCoroutine(ServerPeriodicRefresh_Coroutine());
				return;
			}

			LatencyTrackerCoroutine = __instance.StartCoroutine(ClientPingReport_Coroutine());
		}

		/// <summary>
		/// Fetch the latency using UnityTransport's RTT
		/// </summary>
		/// <returns>Boolean representing whether the latency is successfully fetched</returns>
		private static bool FetchUnityLatency()
		{
			if (UnityTransportObject == null)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, "UnityTransportObject is null, Latency Patch will be disabled !");
				HUDManagerPatch.DisplayHUDWarning("Unable to find UnityTransport, Latency Patch will be disabled.");

				return false;
			}

			ulong latency = UnityTransportObject.GetCurrentRtt(0);

			// Check if we should measure RTT instead of one-way latency
			if (LCDirectLan.GetConfig<bool>("Latency HUD", "RTTMeasurement"))
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"Our RTT Latency (Unity): {latency}ms");
			}
			else
			{
				latency /= 2;
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"Our One-Way Latency (Unity): {latency}ms");
			}

			// Update the HUD
			HUDManagerPatch.UpdateLatencyHUD((ushort)latency, false);

			return true;
		}

		/// <summary>
		/// Stop tracking latency
		/// </summary>
		/// <param name="__instance">A MonoBehaviour instance in order to be able to stop coroutines</param>
		public static void StopTrackingLatency(MonoBehaviour __instance)
		{
			if (__instance == null)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, "Cannot stop LatencyRPC.LatencyTrackerCoroutine, MonoBehaviour instance is null !");
				LatencyTrackerCoroutine = null;
				return;
			}

			if (LatencyTrackerCoroutine == null)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, "LatencyRPC.LatencyTrackerCoroutine is already stopped !");
				return;
			}

			__instance.StopCoroutine(LatencyTrackerCoroutine);
			LatencyTrackerCoroutine = null;
		}

		/// <summary>
		/// Coroutine that runs on the client to periodically send ping request to the server
		/// </summary>
		private static IEnumerator ClientPingReport_Coroutine()
		{
			bool HasSentHUDWarning = false;

			while (true)
			{
				// Check if we should just use UnityTransport's RTT
				if (!UseCustomLatencyRPC)
				{
					bool FetchSuccess = FetchUnityLatency();
					if (!FetchSuccess)
					{
						// Unity Transport also failed, just disable latency tracking at this point
						LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, "Failed to fetch UnityTransport's RTT, disabling latency tracking...");
						HUDManagerPatch.DestroyLatencyHUD();
						yield break;
					}

					// Sleep until the next polling interval before fetching the next latency
					yield return new WaitForSeconds(CLIENT_PING_INTERVAL_SECONDS);
					continue;
				}

				SendClientPingRequest();

				yield return new WaitForSeconds(CLIENT_PING_INTERVAL_SECONDS);

				// Check if we haven't waited for too long
				long MilisSinceLastPing = GetCurrentEpochMilis() - ServerLastPingToMeTimestamp;
				if (MilisSinceLastPing < 0) MilisSinceLastPing = 0;

				long SecondsSinceLastPing = MilisSinceLastPing / 1000;
				if (SecondsSinceLastPing < SLOW_SERVER_THRESHOLD_SECONDS)
				{
					HasSentHUDWarning = false;
					continue;
				}

				// If Server haven't responded to any of our ping request, they may not support LatencyRPC
				if (ServerLastPingToMeTimestamp == 0)
				{
					LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, "Server did not send global latency refresh, it may not support LatencyRPC !");
					HUDManagerPatch.DisplayHUDWarning("Server did not support LatencyRPC, switching to UnityTransport...");

					// Get the UnityTransport object
					UnityTransportObject = GameObject.Find("NetworkManager").GetComponent<UnityTransport>();

					// Switch to use UnityTransport's RTT
					UseCustomLatencyRPC = false;

					// Continue to the next polling interval
					continue;
				}

				LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, "Server have not sent global latency refresh again !");
				HUDManagerPatch.UpdateLatencyHUD((ushort)MilisSinceLastPing, null);

				if (!HasSentHUDWarning)
				{
					// Send a warning to the HUD
					HUDManagerPatch.DisplayHUDWarning("Server stopped responding to ping, slow server or connection lost ?");
					HasSentHUDWarning = true;
				}

				// Throttle retrying the ping request
				yield return new WaitForSeconds(CLIENT_PING_INTERVAL_SECONDS * 2);
			}
		}

		/// <summary>
		/// Coroutine that runs on the server to periodically broadcast all clients' latency
		/// </summary>
		private static IEnumerator ServerPeriodicRefresh_Coroutine()
		{
			while (true)
			{
				SendGlobalLatencyRefresh();

				yield return new WaitForSeconds(SERVER_GLOBAL_LATENCY_REFRESH_INTERVAL_SECONDS);
			}
		}

		/// <summary>
		/// Function that handles ping request from client
		/// </summary>
		/// <param name="ClientID">The NetworkClient's ID</param>
		/// <param name="reader">FastBufferReader that contains the payload</param>
		[ServerRpc]
		public static void OnClient_PingRequest(ulong ClientID, FastBufferReader reader)
		{
			/** OnClientPingRequest to Server Event Payload:
			 *		byte 1-8: ClientID
			 **/
			if (!reader.TryBeginRead(8))
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, $"Failed to read ping request from ClientID {ClientID} !");
				return;
			}

			reader.ReadValue(out ulong PingRequestClientID);

			if (PingRequestClientID != ClientID)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, $"ClientID {ClientID} sent a ping request with invalid ClientID {PingRequestClientID} !");
				return;
			}

			int PlayerID = allPlayerScripts.GetActualPlayerIndex(ClientID);

			if (PlayerID == -1)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, $"Couldn't find PlayerID for ClientID {ClientID} !");
				return;
			}

			// Get the player's last ping to server
			long LastPingToServer = PlayersLastPingToServer[PlayerID];
			PlayersLastPingToServer[PlayerID] = GetCurrentEpochMilis();

			if (LastPingToServer == 0)
			{
				return;
			}

			// Calculate the latency from the last ping to server
			long latency = GetCurrentEpochMilis() - LastPingToServer;

			// As client only ping every CLIENT_PING_INTERVAL_SECONDS, substraction is needed
			long expectedMaximumLatencyOffset = (long)CLIENT_PING_INTERVAL_SECONDS * 1000;
			latency -= expectedMaximumLatencyOffset;

			// Check if can we fit the latency data into ushort
			if (latency > ushort.MaxValue)
			{
				latency = ushort.MaxValue;
			}

			// Avoid negative latency values in case of time drift (usually caused by inconsistent game timing)
			else if (latency < 0)
			{
				latency = 0;
			}

			// Update the player's latency
			PlayersLatency[PlayerID] = (ushort)latency;

			string username = allPlayerScripts.GetPlayerUsername(PlayerID);

			// If username is Player #${PlayerID}, then we don't have the username yet
			if (username == $"Player #{PlayerID}")
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"Player #{PlayerID}'s latency: {latency}ms");
				return;
			}

			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"{username}'s latency: {latency}ms");
		}

		/// <summary>
		/// Send a ping request to the server as a client
		/// </summary>
		private static void SendClientPingRequest()
		{
			if (NetworkManager.Singleton == null)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, "NetworkManager.Singleton is null, cannot send ClientPingRequest !");
				return;
			}

			// And we are a client
			if (!NetworkManager.Singleton.IsClient)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, "Can only send a ClientPingRequest as a client !");
				return;
			}

			FastBufferWriter writer = new FastBufferWriter(8, Allocator.Temp);
			writer.WriteValue(NetworkManager.Singleton.LocalClient.ClientId);

			NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(LCDirectLan.PLUGIN_NAME + "_OnClientPingRequest_ToServerRpc", 0, writer, NetworkDelivery.Unreliable);
		}

		/// <summary>
		/// Function that handles global latency refresh from server
		/// </summary>
		/// <param name="ClientID">The Server NetworkClient's ID (should always be 0)</param>
		/// <param name="reader">FastBufferReader that contains the payload</param>
		[ClientRpc]
		public static void OnServer_GlobalLatencyRefresh(ulong ClientID, FastBufferReader reader)
		{
			/** GlobalLatencyRefresh to Client Event Payload:
			 *		byte 1: Player count
			 *		byte 2-3: Player #1's latency
			 *		byte 4-5: Player #2's latency
			 *		...
			 **/
			if (ClientID != 0)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, "Got GlobalLatencyRefresh from non-server client !");
				return;
			}

			if (!reader.TryBeginRead(1))
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, "Failed to read player count from GlobalLatencyRefresh !");
				return;
			}

			reader.ReadByte(out byte PlayerCount);

			for (int i = 0; i < PlayerCount; i++)
			{
				if (!reader.TryBeginRead(2))
				{
					LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, $"Failed to read Player #{i + 1}'s latency from GlobalLatencyRefresh !");
					return;
				}

				reader.ReadValue(out ushort PlayerLatency);
				PlayersLatency[i] = PlayerLatency;

				// If this is our own latency, also update the UI
				if (StartOfRound.Instance.allPlayerScripts[i].IsOwner)
				{
					HUDManagerPatch.UpdateLatencyHUD(PlayerLatency, true);
				}
			}

			ServerLastPingToMeTimestamp = GetCurrentEpochMilis();
		}

		/// <summary>
		/// Send a global latency refresh to all clients as a server
		/// </summary>
		private static void SendGlobalLatencyRefresh()
		{
			if (NetworkManager.Singleton == null)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, "NetworkManager.Singleton is null, cannot send GlobalLatencyRefresh !");
				return;
			}

			if (!NetworkManager.Singleton.IsServer)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, "Can only send a GlobalLatencyRefresh as a server !");
				return;
			}

			FastBufferWriter writer = new FastBufferWriter(1 + (PlayersLatency.Length * 2), Allocator.Temp);
			writer.WriteByte((byte)PlayersLatency.Length);

			for (int i = 0; i < PlayersLatency.Length; i++)
			{
				writer.WriteValue(PlayersLatency[i]);
			}

			NetworkManager.Singleton.CustomMessagingManager.SendNamedMessageToAll(LCDirectLan.PLUGIN_NAME + "_GlobalLatencyRefresh_ToClientRpc", writer, NetworkDelivery.Reliable);
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
