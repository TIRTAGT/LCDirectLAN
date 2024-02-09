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

namespace LCDirectLAN.Patches.CustomUsername
{
	internal class StartOfRoundPatch
	{
		/// <summary>
		/// Because the player name on the map screen is already set BEFORE ConnectClientToPlayerObject is called, we need to update it again after the username is set.
		/// </summary>
		public static void LatePatch_PlayerNameOnMapScreen()
		{
			// Only update the player name on our side when there is a targeted player
			if (StartOfRound.Instance.mapScreen.targetedPlayer == null) { return; }

			StartOfRound.Instance.mapScreenPlayerName.text = "MONITORING: " + StartOfRound.Instance.mapScreen.targetedPlayer.playerUsername;
		}
	}
}
