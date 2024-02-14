﻿/**
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
using TMPro;
using UnityEngine;

namespace LCDirectLAN.Patches.LatencyHUD
{
	[HarmonyPatch(typeof(HUDManager))]
	internal class HUDManagerPatch
	{
		private static GameObject LatencyHUD = null;
		private static TextMeshProUGUI LatencyHUD_TMP = null;
		private static ulong LatencyValue = 0;

		[HarmonyPatch("Awake")]
		[HarmonyPostfix]
		[HarmonyPriority(Priority.VeryLow)]
		public static void Postfix_Awake(HUDManager __instance)
		{
			// Do not do anything when not in LAN mode
			if (!LCDirectLan.IsOnLanMode) { return; }
			
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, "HUDManager.Postfix_Awake()");

			GameObject Container = GameObject.Find("Systems/UI/Canvas/IngamePlayerHUD");

			if (Container == null) {
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, "Cannot find IngamePlayerHUD GameObject !");
				return;
			}

			GameObject a = GameObject.Find("Systems/UI/Canvas/IngamePlayerHUD/LCDirectLAN_LatencyHUD");

			if (a != null) {
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, "LatencyHUD already exists !");
				LatencyHUD = a;
				return;
			}

			CreateLatencyHUD(Container);
		}

		/// <summary>
		/// Create the latency HUD GameObject
		/// </summary>
		/// <param name="Container">The parent GameObject to attach the HUD to</param>
		private static void CreateLatencyHUD(GameObject Container)
		{
			GameObject a = GameObject.Find("Systems/UI/Canvas/IngamePlayerHUD/TopLeftCorner/WeightUI/Weight");

			if (a == null)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, "Cannot find Weight GameObject !");
				return;
			}

			LatencyHUD = GameObject.Instantiate(a, Container.transform);

			float OffsetLocation_X = LCDirectLan.GetConfig<float>("Latency HUD", "Offset_X");
			float OffsetLocation_Y = LCDirectLan.GetConfig<float>("Latency HUD", "Offset_Y");

			LatencyHUD.name = "LCDirectLAN_LatencyHUD";
			LatencyHUD.transform.SetLocalPositionAndRotation(new Vector3(-380 + OffsetLocation_X, 229 + OffsetLocation_Y, 0), LatencyHUD.transform.rotation);

			// Get the TextMeshPro component
			LatencyHUD_TMP = LatencyHUD.GetComponent<TextMeshProUGUI>();

			// Set the text properties
			LatencyHUD_TMP.fontSizeMin = 9;
			LatencyHUD_TMP.fontSize = LCDirectLan.GetConfig<float>("Latency HUD", "TextSize");

			// If font size is less than the minimum, set it to the minimum
			if (LatencyHUD_TMP.fontSize < LatencyHUD_TMP.fontSizeMin) {
				LatencyHUD_TMP.fontSize = LatencyHUD_TMP.fontSizeMin;
			}

			LatencyHUD_TMP.text = "Ping : [Calculating] ms";
			LatencyHUD_TMP.maxVisibleCharacters = 23;
		}

		[HarmonyPatch("Update")]
		[HarmonyPostfix]
		[HarmonyPriority(Priority.VeryLow)]
		public static void Postfix_Update()
		{
			if (!LCDirectLan.IsOnLanMode) { return; }

			if (LatencyHUD_TMP == null) { return; }

			// Update the latency HUD
			// If latency is quite high, change the color to red
			if (LatencyValue > 1500) {
				LatencyHUD_TMP.color = new Color(1, 0, 0, 1);
			}
			else if (LatencyValue > 150) {
				LatencyHUD_TMP.color = new Color(0.9528F, 0.3941F, 0, 1);
			}
			else {
				LatencyHUD_TMP.color = new Color(0, 1, 0, 1);
			}

			LatencyHUD_TMP.text = $"Ping : {LatencyValue} ms";
		}

		/// <summary>
		/// Update the latency HUD value, the actual UI update will be done in the Update() method
		/// </summary>
		/// <param name="latency">The latency value in ms</param>
		public static void UpdateLatencyHUD(ushort latency)
		{
			if (LatencyHUD_TMP == null) { return; }

			LatencyValue = latency;
		}

		/// <summary>
		/// Send a warning to the client when the server processing lag is too high
		/// </summary>
		/// <param name="ProcessingTime">The server processing time in ms</param>
		public static void SendServerLagWarning(ushort ProcessingTime) {
			if (LatencyHUD_TMP == null) { return; }

			// Check if we should notify the client when the server processing lag is too high
			if (!LCDirectLan.GetConfig<bool>("Latency HUD", "ServerLagWarning")) {
				return;
			}

			HUDManager.Instance.DisplayTip("LCDirectLAN - Slow Server", $"Internal Server Processing is slow, this will impact your ping ({ProcessingTime}ms)", false, false);
		}
	}
}