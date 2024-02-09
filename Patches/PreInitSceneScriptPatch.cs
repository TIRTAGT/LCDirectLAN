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

namespace LCDirectLAN.Patches
{
	[HarmonyPatch(typeof(PreInitSceneScript))]
	internal class PreInitSceneScriptPatch
	{
		[HarmonyPatch("ChooseLaunchOption")]
		[HarmonyPostfix]
		[HarmonyPriority(Priority.VeryLow)]
		public static void Postfix_ChooseLaunchOption(PreInitSceneScript __instance, bool online)
		{
			LCDirectLan.IsOnLanMode = !online;

			if (!LCDirectLan.IsOnLanMode)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, $"{LCDirectLan.PLUGIN_NAME} is disabled when game is started on Online (steam) mode");
				return;
			}
		}
	}
}
