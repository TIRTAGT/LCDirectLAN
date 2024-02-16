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

using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace LCDirectLAN
{
	[BepInPlugin(LCDirectLan.PLUGIN_GUID, LCDirectLan.PLUGIN_NAME, LCDirectLan.PLUGIN_VERSION)]
	public class LCDirectLan : BaseUnityPlugin
	{
		public const string PLUGIN_GUID = "TIRTAGT.LCDirectLAN";
		public const string PLUGIN_NAME = "LCDirectLAN";
		/// <summary>
		/// Version of the plugin that follows semantic versioning format<br/>
		/// 
		/// <b>Major</b> - Major version number, incremented when there are significant changes that breaks compatibility<br/>
		/// <b>Minor</b> - Minor version number, incremented when there are changes that breaks compatibility<br/>
		/// <b>Build</b> - Build number, incremented when there are changes that doesn't break any compatibility<br/>
		/// </summary>
		public const string PLUGIN_VERSION  = "1.1.0";
		
		/// <summary>
		/// Version of the plugin assembly that follows "major.minor.build.revision" format<br/>
		/// 
		/// <b>Major</b> - Major version number, incremented when there are significant changes that breaks compatibility<br/>
		/// <b>Minor</b> - Minor version number, incremented when there are changes that breaks compatibility<br/>
		/// <b>Build</b> - Build number, incremented when there are changes that doesn't break any compatibility<br/>
		/// <b>Revision</b> - Revision number, 00000 (for Debug/Development) or 10101 (for Release)<br/>
		/// </summary>
#if DEBUG
		public const string PLUGIN_ASSEMBLY_VERSION  = PLUGIN_VERSION + ".00000";
		public const string PLUGIN_COMPILE_CONFIG = "Debug";
#else
		public const string PLUGIN_ASSEMBLY_VERSION  = PLUGIN_VERSION + ".10101";
		public const string PLUGIN_COMPILE_CONFIG = "Release";
#endif

		private readonly Harmony HarmonyLib = new Harmony(LCDirectLan.PLUGIN_GUID);
		private static LCDirectLan Instance;
		private static bool IsAlreadyAwake = false;
		private static ConfigFile config;

		public static bool IsOnLanMode = false;

		public LCDirectLan()
		{
			if (LCDirectLan.Instance != null)
			{
				this.Logger.LogWarning($"{LCDirectLan.PLUGIN_GUID} is already loaded.");
				return;
			}

			LCDirectLan.Instance = this;
		}

		/// <summary>
		/// Called after the plugin/mod class is initialized on BepInEx (usually before the game starts)
		/// </summary>
		private void Awake()
		{
			if (IsAlreadyAwake)
			{
				this.Logger.LogWarning($"{LCDirectLan.PLUGIN_GUID} is already woken up.");
				return;
			}

			IsAlreadyAwake = true;


			config = new ConfigFile(Paths.ConfigPath + $"/{LCDirectLan.PLUGIN_GUID}.cfg", true)
			{
				SaveOnConfigSet = false
			};

			/* Default network configuration when joining LAN lobbies */
			config.Bind<bool>("Join", "CreateExtraButton", true, new ConfigDescription("Add \"Direct LAN Join\" button to start menu, this will move other buttons on the UI and may not be compatible with other mods that also changes the start UI.\nDisabling this will override the game's default Join button function instead of creating a new button"));
			config.Bind<string>("Join", "DefaultAddress", "127.0.0.1", new ConfigDescription("Default IP/Hostname, change-able in the game"));
			config.Bind<ushort>("Join", "DefaultPort", 7777, new ConfigDescription("Default Port, change-able in the game"));
			config.Bind<bool>("Join", "RememberLastJoinSettings", false, new ConfigDescription("Overwrite the default join configuration values when changed in the game"));
			config.Bind<byte>("Join", "HideRawJoinData", 3, new ConfigDescription("Do not display or save the recently joined server data to avoid Server Leak, useful for streamers.\nThis config accepts a bitwise value\nExamples:\n0: Show anything (Disabled/No Hiding)\n1: Hide IP Address\n2. Hide Port Number\n3. Hide IP and Port\n4. Hide Hostname\n7. Hide all of them (IP,Port,Hostname)", new AcceptableValueList<byte>(new byte[] { 0, 1, 2, 3, 4, 7 })));

			/* Default network configuration when hosting LAN lobbies */
			config.Bind<bool>("Host", "ListenOnIPv6", false, new ConfigDescription("Should the game listen on IPv6 when hosting instead of IPv4 ?\nDual-Stack Listening does not seem to be supported by default, custom implementation coming soon ?"));
			config.Bind<ushort>("Host", "DefaultPort", 7777, new ConfigDescription("Default Port for hosting, the default vanilla port is 7777"));
			
			/* Custom Username Feature / Patches */
			config.Bind<bool>("Custom Username", "Enabled", false, new ConfigDescription("Enable CustomUsernamePatch ?\nThis patch requires both Server and Client(s) to work correctly, but having this enabled doesn't interfere with vanilla players (or players that didn't have this enabled)."));
			config.Bind<string>("Custom Username", "HostDefaultUsername", "Lethal Hoster", new ConfigDescription("Default Username when Hosting a game"));
			config.Bind<string>("Custom Username", "JoinDefaultUsername", "Lethal Player", new ConfigDescription("Default Username when Joining a game\nChange-able in the game"));
			config.Bind<bool>("Custom Username", "MergeDefaultUsername", false, new ConfigDescription("Copy/Merge/Use JoinDefaultUsername as HostDefaultUsername too"));
			
			/* Game Join/Connect Timeout settings */
			config.Bind<bool>("Unity Networking", "Enabled", false, new ConfigDescription("Enable UnityNetworkingPatch ?"));
			config.Bind<short>("Unity Networking", "ConnectTimeout", -1, new ConfigDescription("Override the game connect/join timeout in seconds (any values lower than 1 will disable this)"));

			/* Latency HUD Patches */
			config.Bind<bool>("Latency HUD", "Enabled", true, new ConfigDescription("Enable LatencyHUDPatch ?"));
			config.Bind<bool>("Latency HUD", "DisableCustomLatencyRPC", false, new ConfigDescription("Disable LCDirectLAN's custom RPC and replace use UnityTransport's GetCurrentRtt() for measuring latency, additional warning feature will be disabled too"));
			config.Bind<bool>("Latency HUD", "RTTMeasurement", true, new ConfigDescription("Measure Round Trip Time (RTT) instead of one-way latency, which is a more accurate latency representation"));
			config.Bind<bool>("Latency HUD", "DisplayWarningOnFailure", true, new ConfigDescription("Display an in-game warning when there is a problem with LatencyHUDPatch functionality"));
			config.Bind<float>("Latency HUD", "Offset_X", 0.0F, new ConfigDescription("Adjust the X position of the Latency HUD\nHigher value moves the HUD to the right, lower value moves the HUD to the left"));
			config.Bind<float>("Latency HUD", "Offset_Y", 0.0F, new ConfigDescription("Adjust the Y position of the Latency HUD\nHigher value moves the HUD to the top, lower value moves the HUD to the bottom"));
			config.Bind<float>("Latency HUD", "TextSize", 13.0F, new ConfigDescription("Adjust font size of the Latency HUD (Minimum: 9)"));

			config.Save();

			HarmonyLib.PatchAll(typeof(Patches.PreInitSceneScriptPatch));
			HarmonyLib.PatchAll(typeof(Patches.ConfigurableLAN.MenuManagerPatch));

			// Only apply username patches if user wants to
			if (GetConfig<bool>("Custom Username", "Enabled"))
			{
				HarmonyLib.PatchAll(typeof(Patches.CustomUsername.PlayerControllerBPatch));
				HarmonyLib.PatchAll(typeof(Patches.CustomUsername.UsernameRPC));
			}

			// Only apply Unity Networking when the user wants to
			if (GetConfig<bool>("Unity Networking", "Enabled"))
			{
				HarmonyLib.PatchAll(typeof(Patches.UnityNetworking.UnityTransportPatch));
			}

			// Only apply Latency HUD patches when the user wants to
			if (GetConfig<bool>("Latency HUD", "Enabled"))
			{
				HarmonyLib.PatchAll(typeof(Patches.LatencyHUD.HUDManagerPatch));
				HarmonyLib.PatchAll(typeof(Patches.LatencyHUD.LatencyRPC));
			}

			this.Logger.LogInfo("sucessfully loaded.");
		}

		/// <summary>
		/// Send a log to the exposed BepInEx Logging system
		/// </summary>
		/// <param name="level">The BepInEx LogLevel</param>
		/// <param name="data">The log data</param>
		public static void Log(BepInEx.Logging.LogLevel level, object data)
		{
			LCDirectLan.Instance.Logger.Log(level, data);
		}

		/// <summary>
		/// Get a value from Main section of the current BepInEx Runtime Configuration
		/// </summary>
		/// <typeparam name="T">The config data type</typeparam>
		/// <param name="key">The config key</param>
		/// <returns>The value casted to the data type, otherwise the default value for that data type</returns>
		public static T GetConfig<T>(string key)
		{
			return LCDirectLan.GetConfig<T>("Main", key);
		}

		/// <summary>
		/// Get a value from the current BepInEx Runtime Configuration
		/// </summary>
		/// <typeparam name="T">The config data type</typeparam>
		/// <param name="section">The config section</param>
		/// <param name="key">The config key</param>
		/// <returns>The value casted to the data type, otherwise the default value for that data type</returns>
		public static T GetConfig<T>(string section, string key)
		{
			if (config == null) { return default; }


			if (config.TryGetEntry<T>(section, key, out ConfigEntry<T> a))
			{
				return a.Value;
			}

			LCDirectLan.Log(LogLevel.Warning, $"Cannot get config key {key}, no such key on {section}");
			return default;
		}

		/// <summary>
		/// Set a new value on the current BepInEx Runtime Configuration
		/// </summary>
		/// <typeparam name="T">The config data type</typeparam>
		/// <param name="key">The config key</param>
		/// <param name="value">The new config value</param>
		/// <returns>True on success, false on failure</returns>
		public static bool SetConfig<T>(string key, T value)
		{
			return LCDirectLan.SetConfig<T>(key, value);
		}

		/// <summary>
		/// Set a new value on the current BepInEx Runtime Configuration
		/// </summary>
		/// <typeparam name="T">The config data type</typeparam>
		/// <param name="section">The config section</param>
		/// <param name="key">The config key</param>
		/// <param name="value">The new config value</param>
		/// <returns>True on success, false on failure</returns>
		public static bool SetConfig<T>(string section, string key, T value)
		{
			if (config == null) { return false; }


			if (!config.TryGetEntry<T>(section, key, out ConfigEntry<T> a))
			{
				LCDirectLan.Log(LogLevel.Warning, $"Cannot set config key {key} to {value}, no such key on {section}");
				return false;
			}

			a.Value = value;
			return true;
		}

		/// <summary>
		/// Writes current BepInEx Runtime Configuration to disk
		/// </summary>
		public static void SaveConfig()
		{
			if (config == null) { return; }

			config.Save();
		}
	}
}
