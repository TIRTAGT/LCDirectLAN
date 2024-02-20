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

using System;
using HarmonyLib;

namespace LCDirectLAN.Patches
{
	[HarmonyPatch(typeof(PreInitSceneScript))]
	internal class PreInitSceneScriptPatch
	{
		private static Action LateInjector = null;
		private static bool HasAlreadyLaunched = false;

		[HarmonyPatch("ChooseLaunchOption")]
		[HarmonyPostfix]
		[HarmonyPriority(Priority.VeryLow)]
		public static void Postfix_ChooseLaunchOption(PreInitSceneScript __instance, bool online)
		{
			if (HasAlreadyLaunched) { return; }

			HasAlreadyLaunched = true;
			LCDirectLan.IsOnLanMode = !online;

			if (!LCDirectLan.IsOnLanMode)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, $"{LCDirectLan.PLUGIN_NAME} is disabled when game is started on Online (steam) mode");
				return;
			}

			LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, $"{LCDirectLan.PLUGIN_NAME} is enabled");

			// Inject the late patch if we are using Late Patching behavior
			LateInjector?.BeginInvoke(null, null);

			// Remove the late injector reference
			LateInjector = null;
		}

		public static void SetLateInjector(Action action)
		{
			LateInjector = action;
		}
	}
}
