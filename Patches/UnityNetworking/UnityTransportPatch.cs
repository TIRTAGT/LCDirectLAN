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

using HarmonyLib;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

namespace LCDirectLAN.Patches.UnityNetworking
{
	[HarmonyPatch(typeof(NetworkManager))]
	internal class UnityTransportPatch
	{
		[HarmonyPatch("Awake")]
		[HarmonyPostfix]
		[HarmonyPriority(Priority.VeryLow)]
		public static void Postfix_Awake(NetworkManager __instance)
		{
			// Do not do anything when not in LAN mode
			if (!LCDirectLan.IsOnLanMode) { return; }

			UnityTransport UTP = __instance.GetComponent<UnityTransport>();

			if (UTP == null)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, "Cannot modify ConnectTimeout, cannot find NetworkManager !");
				return;
			}

			// Check if we should modify default join/connect timeout
			short ModifyTimeout = LCDirectLan.GetConfig<short>("Unity Networking", "ConnectTimeout");

			if (ModifyTimeout >= 1)
			{
				UTP.MaxConnectAttempts = ModifyTimeout;
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, $"Modified UnityTransport.MaxConnectAttempts to {ModifyTimeout}");
			}
		}
	}
}
