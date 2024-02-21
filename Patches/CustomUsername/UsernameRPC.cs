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
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace LCDirectLAN.Patches.CustomUsername
{
	internal class UsernameRPC : NetworkBehaviour
	{
		/// <summary>
		/// A array of boolean flags to keep track of which player have successfully synced their username to the server
		/// </summary>
		private static bool[] PlayerSyncStatus = new bool[0];
		
		private static byte PlayerSyncBroadcastID = 0;

		/// <summary>
		/// Specify the maximum time to wait for all clients to sync their usernames to the server, in seconds
		/// <br></br>
		/// Must be a value between <b>1</b> and <b>255</b>
		/// <br></br><br></br>
		/// 
		/// When this timeout is reached, the server will display a warning message to the host that some clients may not have synced their usernames
		/// </summary>
		private static readonly byte PlayerSyncWaitTimeout = 30;

		private static bool IsBroadcastingPlayerUsernames = false;
		/// <summary>
		/// Inject the UsernameRPC on PlayerControllerB's ConnectClientToPlayerObject()
		/// </summary>
		/// <param name="__instance"></param>
		[HarmonyPatch(typeof(PlayerControllerB), "ConnectClientToPlayerObject")]
		[HarmonyPrefix]
		[HarmonyPriority(Priority.High)]
		public static void Prefix_ConnectClientToPlayerObject(PlayerControllerB __instance)
		{
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"UsernameRPC.Prefix_ConnectClientToPlayerObject({__instance.NetworkManager.LocalClientId})");

			if (__instance.NetworkManager == null || !__instance.NetworkManager.IsListening)
			{
				return;
			}

			// If this is player is not controlled by us
			if (!__instance.IsOwner) { return; }

			// Listen for server side username refresh events (for syncing)
			NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(LCDirectLan.PLUGIN_NAME + "_GlobalUsernameRefresh_ToClientRpc", new CustomMessagingManager.HandleNamedMessageDelegate(GlobalUsernameRefresh_ToClientRpc));
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"Listening _GlobalUsernameRefresh_ToClientRpc()");

			// Do not allow clients to handle server events
			if (!__instance.NetworkManager.IsServer) { return; }

			// Initialize the PlayerSyncStatus array
			PlayerSyncStatus = new bool[StartOfRound.Instance.allPlayerScripts.Length];
			for (int i = 0; i < PlayerSyncStatus.Length; i++) { PlayerSyncStatus[i] = false; }

			// Listen for username change events
			NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(LCDirectLan.PLUGIN_NAME + "_ClientUsernameChanged_ToServerRpc", new CustomMessagingManager.HandleNamedMessageDelegate(ClientUsernameChanged_ToServerRpc));
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"Listening _ClientUsernameChanged_ToServerRpc()");

			// Listen for username refresh callback events
			NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(LCDirectLan.PLUGIN_NAME + "_ClientReceivedUsernameRefresh_ToServerRpc", new CustomMessagingManager.HandleNamedMessageDelegate(ClientReceivedUsernameRefresh_ToServerRpc));
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"Listening _ClientReceivedUsernameRefresh_ToServerRpc()");
		}

		/// <summary>
		/// Event handler for the client request to update their username on the server and broadcast to other clients
		/// </summary>
		/// <param name="ClientID">The client who sent username change event to the server</param>
		/// <param name="reader"></param>
		[ServerRpc]
		public static void ClientUsernameChanged_ToServerRpc(ulong ClientID, FastBufferReader reader)
		{
			/** This event is called from client to server when the client wants to update it's username (just joined)
			 * 
			 *  ClientUsernameChanged to Server Event Payload:
			 *      byte 1: UsernameLength
			 *      byte 2 - UsernameLength + 1: Usernamed encoded in ASCII bytes
			 **/
			if (!NetworkManager.Singleton.IsServer) { return; }

			int PlayerID = Utility.allPlayerScripts.GetActualPlayerIndex(ClientID);

			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"SERVER: Received username changed event from ClientID {ClientID}, PlayerID {PlayerID} !");

			// Try reading the username length
			if (reader.TryBeginRead(1))
			{
				reader.ReadValue<byte>(out byte UsernameLength);

				if (UsernameLength == 0)
				{
					LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, $"SERVER: Client sent a empty username change request, IGNORED !");
					return;
				}

				if (!reader.TryBeginRead(UsernameLength))
				{
					LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, $"SERVER: Unable to read client username, cannot read {UsernameLength} bytes from reader !");
					return;
				}

				byte[] Username = new byte[UsernameLength];
				reader.ReadBytes(ref Username, UsernameLength);
				string a = Encoding.ASCII.GetString(Username);

				LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"SERVER: Changing Player #{PlayerID} username to: {a}");

				StartOfRound.Instance.allPlayerScripts[PlayerID].playerUsername = a;
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, $"SERVER: Sucessfully changed Player #{PlayerID}'s username to: {a}");

				SendGlobalUsernameRefreshToClient();
				return;
			}

			LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, $"SERVER: Client sent a broken payload, no UsernameLength !");
		}

		/// <summary>
		/// Event handler for the client's request to refresh their cached players username from the server
		/// </summary>
		/// <param name="ClientID">The client who have received the username refresh broadcast from server</param>
		/// <param name="reader"></param>
		[ServerRpc]
		public static void ClientReceivedUsernameRefresh_ToServerRpc(ulong ClientID, FastBufferReader reader) {
			/** This event is called from client to server when the client received a username refresh from the server
			 * 
			 *  ClientReceivedUsernameRefresh to Server Event Payload:
			 *      byte 1: BroadcastID
			 **/
			if (!NetworkManager.Singleton.IsServer) { return; }

			int PlayerID = Utility.allPlayerScripts.GetActualPlayerIndex(ClientID);

			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"SERVER: Received UsernameBroadcastCallback from Client #{ClientID}, Player #{PlayerID} for BroadcastID #{PlayerSyncBroadcastID} !");

			// If PlayerID is out of range, ignore
			if (PlayerID == -1)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, $"SERVER: Player #{PlayerID} is not controlled by anyone, ignoring UsernameBroadcastCallback !");
				return;
			}

			// Try reading the player ID
			if (reader.TryBeginRead(1))
			{
				reader.ReadValue<byte>(out byte BroadcastID);

				// If the broadcast ID is not the same as the current one, ignore as it's outdated or broken
				if (BroadcastID != PlayerSyncBroadcastID)
				{
					LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, $"SERVER: Player #{PlayerID} is lagging behind broadcast {BroadcastID} !");
					return;
				}

				if (PlayerID >= PlayerSyncStatus.Length)
				{
					LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, $"SERVER: Player #{PlayerID} is out of range in PlayerSyncStatus !");
					return;
				}

				PlayerSyncStatus[PlayerID] = true;
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, $"SERVER: Player #{PlayerID} has successfully synced usernames !");
				return;
			}

			LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, $"SERVER: Player #{PlayerID} sent a broken payload, no BroadcastID !");
		}

		/// <summary>
		/// Send a request to all clients to refresh their cached players username from the server
		/// </summary>
		private static void SendGlobalUsernameRefreshToClient()
		{
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"SendGlobalUsernameRefreshToClient()");

			if (!NetworkManager.Singleton.IsServer) {
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, $"CLIENT: Cannot send global username refresh as a client !!!");
				return;
			}

			if (IsBroadcastingPlayerUsernames) {
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, $"SERVER: Still trying to broadcast player usernames, ignoring request to broadcast again");
				return;
			}

			IsBroadcastingPlayerUsernames = true;

			PlayerSyncStatus = new bool[StartOfRound.Instance.allPlayerScripts.Length];
			for (int i = 0; i < PlayerSyncStatus.Length; i++) { PlayerSyncStatus[i] = false; }

			// Generate a unique ID for this broadcast
			byte SyncBroadcastID = PlayerSyncBroadcastID;
			
			// While the broadcast ID is the same as the previous one, generate a new one
			while (SyncBroadcastID == PlayerSyncBroadcastID) {
				SyncBroadcastID = (byte)Random.Range(0, 255);
			}
			PlayerSyncBroadcastID = SyncBroadcastID;

			LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, $"SERVER: Sending all usernames available on the server to clients (BroadcastID: {PlayerSyncBroadcastID})...");

			List<byte[]> DataBuffer = new List<byte[]>();
			int DataBufferSize = 0;

			// Write the broadcast ID
			DataBuffer.Add(new byte[1] { SyncBroadcastID });
			DataBufferSize += 1;
			
			int PlayerCount = StartOfRound.Instance.allPlayerScripts.Length;

			// Write the player count
			DataBuffer.Add(new byte[1] { (byte)PlayerCount });
			DataBufferSize += 1;

            for (int i = 0; i < PlayerCount; i++)
            {
				// If player isn't conntrolled by anyone, send empty username
				if (!StartOfRound.Instance.allPlayerScripts[i].isPlayerControlled) {
					LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"SERVER: Ignoring Player #{i} as they are not controlled by anyone");

					// Write the username length
					DataBuffer.Add(new byte[1] { 0 });
					DataBufferSize += 1;
					continue;
				}

				string Username = StartOfRound.Instance.allPlayerScripts[i].playerUsername;
				int UsernameLength = Username.Length;

				// Write the username length
				DataBuffer.Add(new byte[1] { (byte)UsernameLength });
				DataBufferSize += 1;

				// Encode the username as ASCII and then write it
				DataBuffer.Add(Encoding.ASCII.GetBytes(Username));
				DataBufferSize += UsernameLength;

				LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"SERVER: Packed Player #{i} username: {Username}");
			}

			FastBufferWriter writer = new FastBufferWriter(DataBufferSize, Allocator.Temp);

			// Write the buffer
			for (int i = 0; i < DataBuffer.Count; i++)
			{
				writer.WriteBytes(DataBuffer[i], DataBuffer[i].Length, 0);
			}

			// Start waiting for all clients to sync their usernames
			NetworkManager.Singleton.StartCoroutine(WaitForAllClientsToSyncUsernames());

			NetworkManager.Singleton.CustomMessagingManager.SendNamedMessageToAll(LCDirectLan.PLUGIN_NAME + "_GlobalUsernameRefresh_ToClientRpc", writer, NetworkDelivery.Reliable);
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, $"SERVER: Sucessfully sent a global username refresh to all clients");
			IsBroadcastingPlayerUsernames = false;
		}

		/// <summary>
		/// Send a request to a specific client to refresh their cached players username from the server
		/// </summary>
		/// <param name="NetworkClientID">The ClientID to send the request to</param>
		private static void SendUsernameRefreshToClient(ulong NetworkClientID)
		{
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"SendUsernameRefreshToClient({NetworkClientID})");

			if (!NetworkManager.Singleton.IsServer) {
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, $"CLIENT: Cannot send username refresh as a client !!!");
				return;
			}

			if (IsBroadcastingPlayerUsernames) {
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, $"SERVER: Still trying to broadcast player usernames, ignoring request to broadcast again");
				return;
			}

			IsBroadcastingPlayerUsernames = true;

			// Reuse the previous broadcast ID if it exist, otherwise generate a new one
			byte SyncBroadcastID = PlayerSyncBroadcastID;

			if (SyncBroadcastID == 0) {
				SyncBroadcastID = (byte)Random.Range(0, 255);
				PlayerSyncBroadcastID = SyncBroadcastID;
			}

			LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, $"SERVER: Sending all usernames available on the server to client #{NetworkClientID} (BroadcastID: {PlayerSyncBroadcastID})...");

			List<byte[]> DataBuffer = new List<byte[]>();
			int DataBufferSize = 0;

			// Write the broadcast ID
			DataBuffer.Add(new byte[1] { SyncBroadcastID });
			DataBufferSize += 1;

			int PlayerCount = StartOfRound.Instance.allPlayerScripts.Length;

			// Write the player count
			DataBuffer.Add(new byte[1] { (byte)PlayerCount });
			DataBufferSize += 1;

			for (int i = 0; i < PlayerCount; i++)
			{
				// If player isn't conntrolled by anyone, send empty username
				if (!StartOfRound.Instance.allPlayerScripts[i].isPlayerControlled) {
					LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"SERVER: Ignoring Player #{i} as they are not controlled by anyone");

					// Write the username length
					DataBuffer.Add(new byte[1] { 0 });
					DataBufferSize += 1;
					continue;
				}

				string Username = StartOfRound.Instance.allPlayerScripts[i].playerUsername;
				int UsernameLength = Username.Length;

				// Write the username length
				DataBuffer.Add(new byte[1] { (byte)UsernameLength });
				DataBufferSize += 1;

				// Encode the username as ASCII and then write it
				DataBuffer.Add(Encoding.ASCII.GetBytes(Username));
				DataBufferSize += 1 + UsernameLength;

				LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"SERVER: Packed Player #{i} username: {Username}");
			}

			FastBufferWriter writer = new FastBufferWriter(DataBufferSize, Allocator.Temp);
			
			// Write the buffer
			for (int i = 0; i < DataBuffer.Count; i++)
			{
				writer.WriteBytes(DataBuffer[i], DataBuffer[i].Length, 0);
			}

			NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(LCDirectLan.PLUGIN_NAME + "_GlobalUsernameRefresh_ToClientRpc", NetworkClientID, writer, NetworkDelivery.Reliable);
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, $"SERVER: Sucessfully sent a username refresh to client #{NetworkClientID}");
			IsBroadcastingPlayerUsernames = false;
		}

		/// <summary>
		/// Wait for all clients to sync their usernames to the server
		/// </summary>
		/// <returns>An IEnumerator for the coroutine</returns>
		private static System.Collections.IEnumerator WaitForAllClientsToSyncUsernames()
		{
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"WaitForAllClientsToSyncUsernames()");

			if (!NetworkManager.Singleton.IsServer) {
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, $"CLIENT: Cannot wait for all clients to sync usernames as a client !!!");
				yield break;
			}

			// Wait for a while before checking sync status again (to give time for clients to receive the first broadcast)
			yield return new WaitForSeconds(1);

			// Remember which BroadcastID we are waiting for
			byte TargetBroadcastID = PlayerSyncBroadcastID;

			// Calculate until when we should wait for all clients to sync their usernames
			float TargetWaitTimeout = Time.time + PlayerSyncWaitTimeout;

			// Calculate the interval to refresh the sync status
			int RefreshInterval = PlayerSyncWaitTimeout / 10;

			// Wait for all clients to sync their usernames
			while (true)
			{
				// If the timeout is reached, display a warning message to the host that some clients may not have synced their usernames
				if (Time.time > TargetWaitTimeout)
				{
					LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, $"SERVER: Reached Username Sync Timeout, Some clients may not have synced their usernames !");

					// List all sync status
					for (int i = 0; i < PlayerSyncStatus.Length; i++)
					{
						// Ignore players that are not controlled by anyone
						if (!StartOfRound.Instance.allPlayerScripts[i].isPlayerControlled) { continue; }

						LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, $"SERVER: Is Client #{i} ({StartOfRound.Instance.allPlayerScripts[i].playerUsername}) has reported they are synced: {PlayerSyncStatus[i]}");
					}
					break;
				}

				// If the broadcast ID is not the same as the current one, ignore as we may need to wait for a new broadcast
				if (TargetBroadcastID != PlayerSyncBroadcastID)
				{
					LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, $"SERVER: Broadcast ID mismatch, aborting wait for current sync: {TargetBroadcastID} != {PlayerSyncBroadcastID} !");
					break;
				}

				bool IsAllClientsSynced = true;
				for (int i = 0; i < PlayerSyncStatus.Length; i++)
				{
					// Ignore players that are not controlled by anyone
					if (!StartOfRound.Instance.allPlayerScripts[i].isPlayerControlled) { continue; }

					if (!PlayerSyncStatus[i])
					{
						IsAllClientsSynced = false;

						// Resend the username refresh to clients that haven't synced their usernames
						LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, $"SERVER: Player #{i} ({StartOfRound.Instance.allPlayerScripts[i].playerUsername}) has not reported their username are synced, resending...");
						SendUsernameRefreshToClient(StartOfRound.Instance.allPlayerScripts[i].actualClientId);
						break;
					}
				}

				if (IsAllClientsSynced) { break; }

				// Wait for a while before checking sync status again
				yield return new WaitForSeconds(RefreshInterval);
			}

			if (Time.time > TargetWaitTimeout)
			{
				HUDManager.Instance.DisplayTip("LCDirectLAN", "Some clients may not have synced their usernames properly", false, false);
			}
			else
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, $"SERVER: All clients have successfully synced their usernames !");
				PlayerSyncBroadcastID = 0;
			}
		}

		/// <summary>
		/// Send a request to the server to update our (currently owned player object) username
		/// </summary>
		/// <param name="username">The username</param>
		public static void SendInformationToServerRpc(string username)
		{
			if (NetworkManager.Singleton.IsClient)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, $"CLIENT: Trying to send our username ({username}) to server");

				if (username == string.Empty)
				{
					LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, $"CLIENT: Cannot send empty username !");
					return;
				}

				byte[] b = Encoding.ASCII.GetBytes(username);

				if (b.Length > 255)
				{
					LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, $"CLIENT: Cannot send username, too long ({ b.Length }) !");
					return;
				}

				FastBufferWriter writer = new FastBufferWriter(1 + b.Length, Allocator.Temp);
				// Write the username length
				writer.WriteByte((byte)b.Length);
				writer.WriteBytes(b, b.Length, 0);
				NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(LCDirectLan.PLUGIN_NAME + "_ClientUsernameChanged_ToServerRpc", 0, writer, NetworkDelivery.Reliable);
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"CLIENT: Sucessfully sent our username ({username}) to server");
			}
		}

		/// <summary>
		/// Event handler for the server's request to refresh client cached players from the server
		/// </summary>
		/// <param name="ClientID">The request source (always the server, which is 0)</param>
		/// <param name="reader">The buffer data</param>
		[ClientRpc]
		public static void GlobalUsernameRefresh_ToClientRpc(ulong ClientID, FastBufferReader reader)
		{
			/** This event is called from server to client when there is a update on any of the client username
			 * 
			 *  GlobalUsernameRefresh to Client Event Payload:
			 *      byte 1: BroadcastID
			 *      byte 2: PlayerCount
			 *          byte 3: Player 1's UsernameLength (in game this is Player #0)
			 *          byte 4 - (4 + UsernameLength): Usernamed encoded in ASCII bytes
			 *          
			 *          ==== Multi Player ====
			 *          byte prev + 1: Player 2's UsernameLength (in game this is Player #1)
			 *          byte prev - (prev + UsernameLength): : Usernamed encoded in ASCII bytes
			 *          
			 *          .... 
			 **/

			LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, $"CLIENT: Got GlobalUsernameRefresh from Server !");

			if (!NetworkManager.Singleton.IsClient) { return; }

			if (!reader.TryBeginRead(1))
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, $"CLIENT: Server sent a broken GlobalUsernameRefresh payload, no BroadcastID !");
				return;
			}

			reader.ReadValue(out byte BroadcastID);

			if (!reader.TryBeginRead(1))
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, $"CLIENT: Server sent a broken GlobalUsernameRefresh payload, no PlayerCount !");
				return;
			}

			reader.ReadValue(out byte PlayerCount);

			// Make sure we have the same amount of players as the server
			if (PlayerCount != StartOfRound.Instance.allPlayerScripts.Length)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, $"CLIENT: Server sent a GlobalUsernameRefresh with {PlayerCount} players, but we only have {StartOfRound.Instance.allPlayerScripts.Length} players !");
				return;
			}

			QuickMenuManager quickMenuManager = Object.FindObjectOfType<QuickMenuManager>();

			// Iterate for all players
			for (int i = 0; i < PlayerCount; i++)
			{
				if (!reader.TryBeginRead(1))
				{
					LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, $"CLIENT: Server sent a broken GlobalUsernameRefresh payload, no UsernameLength !\nThe lobby host may be using a mod that increases the maximum player count in lobby that did not get synced to you.");
					return;
				}

				reader.ReadValue(out byte UsernameLength);

				if (UsernameLength == 0)
				{
					// If the player for this username is not controlled by anyone, we know the server will send a empty username
					// So we can ignore this and continue
					if (!StartOfRound.Instance.allPlayerScripts[i].isPlayerControlled) {
						LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"CLIENT: Server ignored username for Player #{i} (Not controlled)");
						continue;
					}

					LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, $"CLIENT: Server sent a empty username for Player #{i}, ignoring !");
					continue;
				}

				if (!reader.TryBeginRead(UsernameLength))
				{
					LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, $"CLIENT: Server sent a broken GlobalUsernameRefresh payload, cannot read {UsernameLength} bytes from reader for the Player #{i} !");
					return;
				}

				byte[] Username = new byte[UsernameLength];
				reader.ReadBytes(ref Username, UsernameLength);
				string a = Encoding.ASCII.GetString(Username);

				LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"CLIENT: Unpacked Player #{i} username: {a}");

				bool IsSomethingFailed = false;

				if (i < StartOfRound.Instance.allPlayerScripts.Length)
				{
					// Change the player's PlayerControllerB instance username
					StartOfRound.Instance.allPlayerScripts[i].playerUsername = a;

					// Change the overhead username displayed in-game (literally over the player's head)
					StartOfRound.Instance.allPlayerScripts[i].usernameBillboardText.text = a;
				}
				else
				{
					IsSomethingFailed = true;
					LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, $"CLIENT: Cannot set StartOfRound.allPlayerScripts[{i}], it only has {StartOfRound.Instance.allPlayerScripts.Length} elements");
				}

				if (i < quickMenuManager.playerListSlots.Length)
				{
					// Change username display on the game pause menu (ESC)
					quickMenuManager.playerListSlots[i].usernameHeader.text = a;
				}
				else
				{
					IsSomethingFailed = true;
					LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, $"CLIENT: Cannot set quickMenuManager.playerListSlots[{i}], it only has {quickMenuManager.playerListSlots} elements");
				}

				if (i < StartOfRound.Instance.mapScreen.radarTargets.Count)
				{
					// Change username display on the ship's monitor
					StartOfRound.Instance.mapScreen.radarTargets[i].name = a;
				}
				else
				{
					IsSomethingFailed = true;
					LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, $"CLIENT: Cannot set StartOfRound.mapScreen.radarTargets[{i}], it only has {StartOfRound.Instance.mapScreen.radarTargets} elements");
				}

				
				if (IsSomethingFailed) { 
					LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, $"CLIENT: Some problem occured while changing Player #{i}'s username to: {a}]\nThe lobby host may be using a mod that increases the maximum player count in lobby that did not get synced to you.");
					return;
				}

				LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, $"CLIENT: Sucessfully changed Player #{i}'s username to: {a}");
			}

			// Sync the currently displayed name on the map screen
			StartOfRoundPatch.LatePatch_PlayerNameOnMapScreen();

			// Send a callback to the server that we have successfully synced our usernames
			FastBufferWriter writer = new FastBufferWriter(1, Allocator.Temp);
			writer.WriteByte(BroadcastID);
			NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(LCDirectLan.PLUGIN_NAME + "_ClientReceivedUsernameRefresh_ToServerRpc", 0, writer, NetworkDelivery.Reliable);
		}
	}
}
