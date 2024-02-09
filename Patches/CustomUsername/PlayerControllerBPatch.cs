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

namespace LCDirectLAN.Patches.CustomUsername
{
	[HarmonyPatch(typeof(PlayerControllerB))]
	internal class PlayerControllerBPatch
	{
		[HarmonyPatch("ConnectClientToPlayerObject")]
		[HarmonyPrefix]
		[HarmonyPriority(Priority.VeryLow)]
		public static void Prefix_ConnectClientToPlayerObject(PlayerControllerB __instance)
		{
			// Do not do anything when not in LAN mode
			if (!LCDirectLan.IsOnLanMode) { return; }

			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"Prefix_ConnectClientToPlayerObject({__instance.actualClientId},{__instance.IsServer},{__instance.IsOwner},{__instance.IsClient})");

			// If this is player is not controlled by us
			if (!__instance.IsOwner)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"Skipped changing username on ClientID {__instance.actualClientId}, because it isn't the local player.");
				return;
			}

			// If we are server, use the default Host instead of default Join username
			if (__instance.IsServer)
			{
				// If we should use the join username for hosting too
				if (LCDirectLan.GetConfig<bool>("Custom Username", "MergeDefaultUsername"))
				{
					__instance.playerUsername = LCDirectLan.GetConfig<string>("Custom Username", "JoinDefaultUsername");
				}
				else
				{
					__instance.playerUsername = LCDirectLan.GetConfig<string>("Custom Username", "HostDefaultUsername");
				}

				LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"ConnectClientToPlayerObject() {__instance.playerUsername} (HOSTING)");

				UsernameRPC.SendInformationToServerRpc(__instance.playerUsername);
				return;
			}

			__instance.playerUsername = LCDirectLan.GetConfig<string>("Custom Username", "JoinDefaultUsername");
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"ConnectClientToPlayerObject() {__instance.playerUsername} (JOINING)");

			UsernameRPC.SendInformationToServerRpc(__instance.playerUsername);
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"Successfully executed SendInformationToServerRpc('{__instance.playerUsername}')");
		}
	}
}
