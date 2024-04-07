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
using UnityEngine;

namespace LCDirectLAN.Utility
{
	internal class GameObjectManager
	{
		/// <summary>
		/// Utility function to ensure a GameObject exists in the scene, also outputs the GameObject reference
		/// </summary>
		/// <param name="path">The path to the GameObject</param>
		/// <param name="obj">The GameObject reference for use</param>
		/// <returns>True if the GameObject exists, False otherwise</returns>
		public static bool EnsureGameObjectExist(string path, out GameObject obj)
		{
			bool exist = IsExist(path, out obj);

			if (!exist)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, $"Cannot find {path} !");
				return false;
			}

			return true;
		}

		/// <summary>
		/// Utility function to ensure a GameObject exists in the scene
		/// </summary>
		/// <param name="name">The path to the GameObject</param>
		/// <param name="obj">The GameObject reference for use</param>
		/// <returns>True if the GameObject exists, False otherwise</returns>
		public static bool IsExist(string name, out GameObject obj)
		{
			obj = GameObject.Find(name);
			return obj != null;
		}

		/// <summary>
		/// Utility function to ensure a GameObject exists in the scene
		/// </summary>
		/// <param name="name">The path to the GameObject</param>
		/// <returns>True if the GameObject exists, False otherwise</returns>
		public static bool IsExist(string name)
		{
			return GameObject.Find(name) != null;
		}

		/// <summary>
		/// Utility function to get a GameObject from the scene
		/// </summary>
		/// <param name="name">The path to the GameObject</param>
		/// <returns>The GameObject reference, null if not found</returns>
		public static GameObject GetGameObject(string name)
		{
			return GameObject.Find(name);
		}

		/// <summary>
		/// Utility function to delete a GameObject from the scene
		/// </summary>
		/// <param name="path">The path to the GameObject</param>
		/// <returns>True if the GameObject is deleted, False otherwise</returns>
		public static bool DeleteGameObject(string path)
		{
			if (!IsExist(path, out GameObject obj)) { return false; }

			GameObject.Destroy(obj);
			return true;
		}

		/// <summary>
		/// Utility function to delete a GameObject from the scene
		/// </summary>
		/// <param name="obj">The GameObject reference</param>
		/// <returns>True if the GameObject is deleted, False otherwise</returns>
		public static bool DeleteGameObject(GameObject obj)
		{
			if (obj == null) { return false; }

			GameObject.Destroy(obj);
			return true;
		}
	}
}
