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

namespace LCDirectLAN.Utility
{
	internal class allPlayerScripts
	{
		/// <summary>
		/// Get the actual Player ID by their Network ClientID
		/// </summary>
		/// <param name="ClientID">The Network ClientID</param>
		/// <returns>The actual player index or -1 on failure</returns>
		public static int GetActualPlayerIndex(ulong ClientID)
		{
			if (StartOfRound.Instance == null) { return -1; }

			for (int i = 0; i < StartOfRound.Instance.allPlayerScripts.Length; i++)
			{
				if (StartOfRound.Instance.allPlayerScripts[i].actualClientId == ClientID)
				{
					return i;
				}
			}

			return -1;
		}

		/// <summary>
		/// Get player username by their ID
		/// </summary>
		/// <param name="PlayerID">The player ID</param>
		/// <returns>The player username or null on failure</returns>
		public static string GetPlayerUsername(int PlayerID)
		{
			if (StartOfRound.Instance == null) { return null; }

			if (PlayerID < 0 || PlayerID >= StartOfRound.Instance.allPlayerScripts.Length) { return null; }

			return StartOfRound.Instance.allPlayerScripts[PlayerID].playerUsername;
		}
	}
}
