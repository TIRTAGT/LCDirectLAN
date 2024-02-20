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
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace LCDirectLAN.Patches.LatencyHUD
{
	[HarmonyPatch(typeof(HUDManager))]
	internal class HUDManagerPatch
	{
		private static GameObject LatencyHUD = null;
		private static TextMeshProUGUI LatencyHUD_TMP = null;
		private static ulong LatencyValue = 0;
		private static bool UpdateLock = false;

		[HarmonyPatch("Awake")]
		[HarmonyPostfix]
		[HarmonyPriority(Priority.VeryLow)]
		public static void Postfix_Awake(HUDManager __instance)
		{
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

			if (NetworkManager.Singleton == null) {
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, "NetworkManager.Singleton is null !");
				return;
			}

			// Check if we shouldn't track latency to ourself
			if (LCDirectLan.GetConfig<bool>("Latency HUD", "HideHUDWhileHosting") && NetworkManager.Singleton.IsServer) {
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, "Latency HUD is disabled while hosting !");
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
			// Lock unnecessary update to prevent performance issues
			if (UpdateLock) { return; }
			UpdateLock = true;

			if (LatencyHUD_TMP == null) { return; }

			// Update the latency HUD
			if (LatencyValue > 600)
			{
				// Change the color to red
				LatencyHUD_TMP.color = new Color(1, 0, 0, 1);
			}
			else if (LatencyValue > 300)
			{
				// Change the color to orange (similar to the game's color scheme)
				LatencyHUD_TMP.color = new Color(0.9528F, 0.3941F, 0, 1);
			}
			else
			{
				// Change the color to green
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
			UpdateLock = false;
		}

		/// <summary>
		/// Send warning using the in-game warning dialog
		/// </summary>
		/// <param name="message">The message of the warning</param>
		public static void DisplayHUDWarning(string message) {
			// Make sure we are allowed to send the warning
			if (!LCDirectLan.GetConfig<bool>("Latency HUD", "DisplayWarningOnFailure")) { return; }

			HUDManager.Instance.DisplayTip("LCDirectLAN - Latency HUD", message, false, false);
		}

		/// <summary>
		/// Destroy the latency HUD GameObject
		/// </summary>
		public static void DestroyLatencyHUD() {
			if (LatencyHUD != null) {
				LatencyHUD.SetActive(false);
				LatencyHUD_TMP = null;
				GameObject.Destroy(LatencyHUD);
				LatencyHUD = null;
			}
		}
	}
}
