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
using System;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using LCDirectLAN.Utility;
using System.Collections;

namespace LCDirectLAN.Patches.ConfigurableLAN
{
	internal class PlayerJoinData {
		public string Address;
		public string Username;

		public PlayerJoinData() {
			Address = "";
			Username = "";
			SetPort("7777");
		}

		public PlayerJoinData(string address = "", string port = "7777", string username = "") {
			Address = address;
			Username = username;
			SetPort(port);
		}

		public PlayerJoinData(string address = "", ushort port = 7777, string username = "") {
			Address = address;
			Username = username;
			SetPort(port.ToString());
		}

		public bool SetPort(string RawPort) {
			if (!ushort.TryParse(RawPort, out ushort p))
			{
				return false;
			}

			if (p < 1) { return false; }

			IsPortValid = true;
			Port = p;
			return true;
		}

		public void ClearPort() {
			Port = 0;
			IsPortValid = false;
		}

		private ushort _Port;
		public ushort Port {
			get {
				return _Port;
			}

			private set {
				_Port = value;
			}
		}
	
		private bool _IsPortValid;
		public bool IsPortValid {
			get {
				return _IsPortValid;
			}

			private set {
				_IsPortValid = value;
			}
		}
	}

	[HarmonyPatch(typeof(MenuManager))]
	internal class MenuManagerPatch
	{
		private static readonly int UsernameLengthLimit = 30;

		private static GameObject DirectJoinObj;
		private static GameObject DirectConnectWindow;

		/// <summary>
		/// 	<para>A bitwise value for server leak protection</para>
		/// 	<br>0: Show anything (Disabled/No Hiding)</br>
		/// 	<br>1: Hide IP Address</br>
		/// 	<br>2: Hide Port Number</br>
		/// 	<br>3: Hide both IP Address and Port</br>
		/// 	<br>4: Hide Hostname</br>
		/// 	<br>7: Hide all of them (IP,Port,Hostname)</br>
		/// </summary>
		private static byte HideJoinData = 3;

		/// <summary>
		/// Variable to store the server join data that is fine to be shown in the UI
		/// </summary>
		private static PlayerJoinData PublicServerJoinData = null;

		/// <summary>
		/// Variable to store the server join data that must not be shown in the UI
		/// </summary>
		private static PlayerJoinData PrivateServerJoinData = null;

		private static bool IsSyncingUIWithData = false;
		
		private static MenuManager __MenuManager;

		[HarmonyPatch("Start")]
		[HarmonyPostfix]
		[HarmonyPriority(Priority.VeryLow)]
		public static void PrepareDirectLANDialog(MenuManager __instance, ref Boolean ___isInitScene)
		{
			if (___isInitScene) { return; }

			// Do not do anything when not in LAN mode
			if (!LCDirectLan.IsOnLanMode) { return; }

			__MenuManager = __instance;

			PublicServerJoinData = new PlayerJoinData(
				LCDirectLan.GetConfig<string>("Join", "DefaultAddress"),
				LCDirectLan.GetConfig<ushort>("Join", "DefaultPort")
			);
			HideJoinData = LCDirectLan.GetConfig<byte>("Join", "HideRawJoinData");

			// If CustomUsernamePatch is enabled, we should also load the default username
			if (LCDirectLan.GetConfig<bool>("Custom Username", "Enabled")) {
				PublicServerJoinData.Username = LCDirectLan.GetConfig<string>("Custom Username", "JoinDefaultUsername");
			}

			LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, "Loaded JoinData from config file:");
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, $"Address: '{PublicServerJoinData.Address}'");
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, $"Port: '{PublicServerJoinData.Port}'");
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, $"Username: '{PublicServerJoinData.Username}'");

			if (HideJoinData > 0) {
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, $"Applying server leak protection based on HideRawJoinData's bitwise value: {HideJoinData}");

				// Check if we should hide IP Address
				if ((HideJoinData & 1) == 1 && !string.IsNullOrEmpty(PublicServerJoinData.Address) && ResolveDNS.CheckIPType(PublicServerJoinData.Address) != System.Net.Sockets.AddressFamily.Unknown) {
					PublicServerJoinData.Address = "";
					LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, "Server IP Address from config is cleared !");
				}

				// Check if we should hide Port Number
				if ((HideJoinData & 2) == 2) {
					PublicServerJoinData.ClearPort();
					LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, "Server Port value from config is cleared !");
				}

				// Check if we should hide Hostname
				if ((HideJoinData & 4) == 4 && ResolveDNS.IsOnHostnameFormat(PublicServerJoinData.Address)) {
					PublicServerJoinData.Address = "";
					LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, "Server Hostname value from config is cleared !");
				}
			}

			// Check if we should create a new button or reuse the original LAN Join button
			if (LCDirectLan.GetConfig<bool>("Join", "CreateExtraButton"))
			{
				CreateDirectJoinButton();
			}
			else {
				ReuseDirectJoinButton();
			}
		}

		/// <summary>
		/// Create a new button for direct LAN join and adjust the position of all other buttons
		/// </summary>
		private static void CreateDirectJoinButton()
		{
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, "Creating a new LAN Join button !");
			GameObject _MainButtons = GameObject.Find("Canvas/MenuContainer/MainButtons");

			if (_MainButtons == null) {
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, "Cannot find MainButtons !");
				return;
			}

			GameObject _HostButton = GameObject.Find("Canvas/MenuContainer/MainButtons/HostButton");

			if (_HostButton == null)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, "Cannot find HostButton !");
				return;
			}

			// Re adjusts position of all menu buttons (using the old position for host button as the place of our new button)
			Vector3 HostButtonNewPos = new Vector3(_HostButton.transform.position.x,  _HostButton.transform.position.y - 3.5F,  _HostButton.transform.position.z);

			ReadjustAllMenuButtons(_MainButtons);

			// Create a duplicate of the HostButton and use the original position
			DirectJoinObj = GameObject.Instantiate(_HostButton, _MainButtons.transform);
			DirectJoinObj.gameObject.name = "DirectJoinButton";
			DirectJoinObj.transform.SetPositionAndRotation(HostButtonNewPos, DirectJoinObj.transform.rotation);
			DirectJoinObj.SetActive(true); // make sure it is not hidden by default

			// Change the text
			TextMeshProUGUI b = (TextMeshProUGUI)DirectJoinObj.transform.GetChild(1).GetComponent("TextMeshProUGUI");
			b.text = "> Direct LAN Join";

			// Listen for onClick event
			Button DirectJoinButton = DirectJoinObj.GetComponent<Button>();
			DirectJoinButton.onClick.RemoveAllListeners();
			DirectJoinButton.onClick.AddListener(new UnityEngine.Events.UnityAction(OnDirectJoinButtonClicked));

			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, "Instantiated DirectJoinButton");
		}

		/// <summary>
		/// Reuse the original LAN Join button by overriding its event listeners
		/// </summary>
		private static void ReuseDirectJoinButton()
		{
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, "Reusing the original LAN Join button !");
			DirectJoinObj = GameObject.Find("Canvas/MenuContainer/MainButtons/StartLAN");

			if (DirectJoinObj == null)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, "Cannot find StartLAN !");
				return;
			}

			Button DirectJoinButton = DirectJoinObj.GetComponent<Button>();
			
			// Disable all persistent listeners
			for (int i = 0; i < DirectJoinButton.onClick.GetPersistentEventCount(); i++) {
				DirectJoinButton.onClick.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
			}

			// Replace all onClick event listeners with our own
			DirectJoinButton.onClick.RemoveAllListeners();
			DirectJoinButton.onClick.AddListener(new UnityEngine.Events.UnityAction(OnDirectJoinButtonClicked));

			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, "Successfully Reused Buttons for DirectJoinButton");
		}

		/// <summary>
		/// Create a dialog window for direct connection menu
		/// </summary>
		private static void CreateDirectConnectWindow()
		{
			GameObject _MenuContainer = GameObject.Find("Canvas/MenuContainer");

			if (_MenuContainer == null)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, "Cannot find MenuContainer !");
				return;
			}

			GameObject _LobbyHostSettings = GameObject.Find("Canvas/MenuContainer/LobbyHostSettings");

			if (_LobbyHostSettings == null)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, "Cannot find LobbyHostSettings !");
				return;
			}

			// Create a duplicate of the HostButton and use the original position
			DirectConnectWindow = GameObject.Instantiate(_LobbyHostSettings, _MenuContainer.transform);
			DirectConnectWindow.gameObject.name = "DirectConnectWindow";
			DirectConnectWindow.transform.SetLocalPositionAndRotation(new Vector3(0, 0, 0), DirectConnectWindow.transform.localRotation);
			DirectConnectWindow.SetActive(false); // make sure it is hidden by default

			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, "Sucessfully Instantiate DirectConnectWindow");

			#region Deleting unused ui child and components

			// Delete the save file panel
			GameObject.Destroy(DirectConnectWindow.transform.GetChild(3).gameObject);

			// Delete the leaderboard panel
			GameObject.Destroy(DirectConnectWindow.transform.GetChild(2).gameObject);

			// Get the DCSettingsContainer
			GameObject DCSettingsContainer = DirectConnectWindow.transform.GetChild(1).gameObject;

			if (DCSettingsContainer == null)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, "Cannot find HostSettingsContainer !");
				return;
			}
			DCSettingsContainer.name = "DCSettingsContainer";
			DCSettingsContainer.GetComponent<RectTransform>().SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 250);
			DCSettingsContainer.GetComponent<RectTransform>().SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 270);

			// Delete PrivatePublicDescription
			GameObject.Destroy(DCSettingsContainer.transform.GetChild(5).gameObject);

			// Delete tipText (it's empty anyway....)
			GameObject.Destroy(DCSettingsContainer.transform.GetChild(2).gameObject);

			GameObject LobbyHostOptions = DCSettingsContainer.transform.GetChild(1).gameObject;

			if (LobbyHostOptions == null)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, "Cannot find LobbyHostOptions !");
				return;
			}

			// Delete LANOptions (it's for hosting a server)
			GameObject.Destroy(LobbyHostOptions.transform.GetChild(1).gameObject);

			GameObject OptionsNormal = LobbyHostOptions.transform.GetChild(0).gameObject;

			// Delete the Public and Private selection
			GameObject.Destroy(OptionsNormal.transform.GetChild(3).gameObject);
			GameObject.Destroy(OptionsNormal.transform.GetChild(4).gameObject);

			// Delete ServerTagInputField
			GameObject.Destroy(OptionsNormal.transform.GetChild(2).gameObject);
			#endregion

			// Change parent of the EnterAName label and ServerNameField
			OptionsNormal.transform.Find("EnterAName").transform.SetParent(DCSettingsContainer.transform, true);
			OptionsNormal.transform.Find("ServerNameField").transform.SetParent(DCSettingsContainer.transform, true);

			// Destroy the OptionsNormal and LobyyHostOptions container
			GameObject.Destroy(OptionsNormal);
			GameObject.Destroy(LobbyHostOptions);

			// Duplicate the text for port
			GameObject AddressInputLabel = DCSettingsContainer.transform.Find("EnterAName").gameObject;
			AddressInputLabel.name = "AddressInputLabel";
			AddressInputLabel.transform.SetLocalPositionAndRotation(new Vector3(-35, 131, 0), AddressInputLabel.transform.localRotation);

			GameObject PortInputLabel = GameObject.Instantiate(AddressInputLabel, DCSettingsContainer.transform);
			PortInputLabel.name = "PortInputLabel";
			PortInputLabel.transform.SetLocalPositionAndRotation(new Vector3(-64, 83, 0), AddressInputLabel.transform.localRotation);

			// Only create the UsernameInputLabel if the user enabled CustomUsernamePatch
			if (LCDirectLan.GetConfig<bool>("Custom Username", "Enabled")) {
				GameObject UsernameInputLabel = GameObject.Instantiate(AddressInputLabel, DCSettingsContainer.transform);
				UsernameInputLabel.name = "UsernameInputLabel";
				UsernameInputLabel.transform.SetLocalPositionAndRotation(new Vector3(-64, 33, 0), AddressInputLabel.transform.localRotation);

				TextMeshProUGUI TMPRouGUI_UsernameInputLabel = UsernameInputLabel.GetComponent<TextMeshProUGUI>();
				TMPRouGUI_UsernameInputLabel.text = "My Username: ";
			}

			GameObject PoweredByLabel = GameObject.Instantiate(AddressInputLabel, DCSettingsContainer.transform);
			PoweredByLabel.name = "LCDirectLANPoweredByLabel";
			PoweredByLabel.transform.SetLocalPositionAndRotation(new Vector3(-0.5F, -110, 0), AddressInputLabel.transform.localRotation);

			TextMeshProUGUI TMPRoUGUI_PortInputLabel = PortInputLabel.GetComponent<TextMeshProUGUI>();
			TMPRoUGUI_PortInputLabel.text = "Server Port:";

			TextMeshProUGUI TMPRoUGUI_AddressInputLabel = AddressInputLabel.GetComponent<TextMeshProUGUI>();
			TMPRoUGUI_AddressInputLabel.text = "Server IP/Hostname:";

			TextMeshProUGUI TMPRouGUI_PoweredByLabel = PoweredByLabel.GetComponent<TextMeshProUGUI>();
			TMPRouGUI_PoweredByLabel.text = "Powered by TIRTAGT/LCDirectLAN";
			TMPRouGUI_PoweredByLabel.alpha = 0.3F;
			TMPRouGUI_PoweredByLabel.fontSizeMin = 13;
			TMPRouGUI_PoweredByLabel.fontSize = 13;

			// Duplicate the ServerNameField as the ServerPortField
			GameObject ServerNameField_GameObject = DCSettingsContainer.transform.Find("ServerNameField").gameObject;
			ServerNameField_GameObject.transform.SetLocalPositionAndRotation(new Vector3(0, 110, 0), ServerNameField_GameObject.transform.localRotation);
			TMP_InputField ServerNameInputField = ServerNameField_GameObject.GetComponent<TMP_InputField>();
			((TextMeshProUGUI)ServerNameInputField.placeholder).text = "";
			ServerNameInputField.text = "";
			ServerNameInputField.onValueChanged.AddListener(new UnityEngine.Events.UnityAction<string>(OnServerNameInputField_Changed));

			GameObject ServerPortField_GameObject = GameObject.Instantiate(ServerNameField_GameObject, DCSettingsContainer.transform);
			ServerPortField_GameObject.name = "ServerPortField";
			ServerPortField_GameObject.transform.SetLocalPositionAndRotation(new Vector3(0, 62, 0), ServerPortField_GameObject.transform.localRotation);
			TMP_InputField ServerPortInputField = ServerPortField_GameObject.GetComponent<TMP_InputField>();
			((TextMeshProUGUI)ServerPortInputField.placeholder).text = "7777";
			ServerPortInputField.text = "";
			ServerPortInputField.onValueChanged.AddListener(new UnityEngine.Events.UnityAction<string>(OnServerPortInputField_Changed));

			// Only show the custom username field if the user enabled CustomUsernamePatch
			if (LCDirectLan.GetConfig<bool>("Custom Username", "Enabled")) {
				GameObject CustomUsernameField_GameObject = GameObject.Instantiate(ServerNameField_GameObject, DCSettingsContainer.transform);
				CustomUsernameField_GameObject.name = "CustomUsernameField";
				CustomUsernameField_GameObject.transform.SetLocalPositionAndRotation(new Vector3(0, 10.5F, 0), CustomUsernameField_GameObject.transform.localRotation);
				TMP_InputField CustomUsernameInputField = CustomUsernameField_GameObject.GetComponent<TMP_InputField>();
				((TextMeshProUGUI)CustomUsernameInputField.placeholder).text = "Lethal Player";
				CustomUsernameInputField.text = LCDirectLan.GetConfig<string>("Custom Username", "JoinDefaultUsername");
				CustomUsernameInputField.characterLimit = UsernameLengthLimit;
				CustomUsernameInputField.onValueChanged.AddListener(new UnityEngine.Events.UnityAction<string>(OnUsernameInputField_Changed));
			}

			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, "Sucessfully Instantiate input fields");
			// Remove all listener for Confirm and Back button, and change the text.
			GameObject ConfirmButtonObj = DCSettingsContainer.transform.Find("Confirm").gameObject;
			ConfirmButtonObj.transform.SetLocalPositionAndRotation(new Vector3(0, -50, 0), ConfirmButtonObj.transform.localRotation);

			Button ConfirmButton = ConfirmButtonObj.GetComponent<Button>();

			if (ConfirmButton == null)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, "Cannot create Button component for ConfirmButton !");
				return;
			}

			// There is a persistent event listener on this button, disable it.
			ConfirmButton.onClick.SetPersistentListenerState(0, UnityEngine.Events.UnityEventCallState.Off);
			ConfirmButton.onClick.AddListener(new UnityEngine.Events.UnityAction(OnDirectJoinConnect));
			ConfirmButton.GetComponent<RectTransform>().SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 250);
			ConfirmButton.transform.Find("SelectionHighlight").SetLocalPositionAndRotation(new Vector3(0.5F, 0.443F, 0.798F), ConfirmButton.transform.Find("SelectionHighlight").transform.localRotation);
			ConfirmButton.transform.Find("SelectionHighlight").GetComponent<RectTransform>().sizeDelta = new Vector2(210, 25.191F);

			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, "Sucessfully taken care of onClickListener on ConfirmButtonObj");

			TextMeshProUGUI TMP_ConfirmButton = ConfirmButtonObj.transform.GetChild(1).gameObject.transform.GetComponent<TextMeshProUGUI>();
			TMP_ConfirmButton.text = "[ Connect To Server ]";

			GameObject BackButtonObj = DCSettingsContainer.transform.Find("Back").gameObject;
			BackButtonObj.transform.SetLocalPositionAndRotation(new Vector3(0, -90, 0), BackButtonObj.transform.localRotation);

			Button BackButton = BackButtonObj.GetComponent<Button>();
			BackButton.onClick.RemoveAllListeners();
			BackButton.onClick.AddListener(new UnityEngine.Events.UnityAction(OnDirectJoinBack));

			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, "Sucessfully Replaced onClickListener on BackButtonObj");

			SetInputTextField(ServerNameField_GameObject, PublicServerJoinData.Address);
			
			if (PublicServerJoinData.IsPortValid) {
				SetInputTextField(ServerPortField_GameObject, PublicServerJoinData.Port.ToString());
			}

			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, "Instantiated DirectConnectSettings");
		}

		/// <summary>
		/// Lower all menu buttons to a specified offset in order to leave some space for the Direct Connect button
		/// </summary>
		/// <param name="_MainButtons">The menu container's Game Object (MainButtons)</param>
		private static void ReadjustAllMenuButtons(GameObject _MainButtons)
		{
			(string, float)[] ButtonKeys = new (string, float)[] { ("HostButton", -3F), ("StartLAN", 3.3F), ("SettingsButton", 2F), ("Credits", 1F)};

			foreach ((string, float) ButtonKey in ButtonKeys)
			{
				Transform temp = _MainButtons.transform.Find(ButtonKey.Item1);

				if (temp == null)
				{
					LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, $"Cannot find {ButtonKey.Item1} in MenuButtons !");
					continue;
				}

				GameObject ButtonObj = temp.gameObject;

				// Lower position of the button
				Vector3 ButtonOldPos = ButtonObj.transform.position;
				Vector3 ButtonNewPost = new Vector3(ButtonOldPos.x, ButtonOldPos.y - ButtonKey.Item2, ButtonOldPos.z);
				ButtonObj.transform.SetPositionAndRotation(ButtonNewPost, ButtonObj.transform.rotation);
			}
		}

		/// <summary>
		/// Get the current text value of a TMP_InputField
		/// </summary>
		/// <param name="TextArea">The GameObject that has TMP_InputField as a component</param>
		/// <returns>The current text value, or empty on failure.</returns>
		private static string GetInputTextField(GameObject TextArea)
		{
			TMP_InputField TextObject = TextArea.gameObject.GetComponent<TMP_InputField>();

			if (TextObject == null)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, "GetTextFromInputField() called on GameObject with no TMP_InputField");
				return "";
			}

			string a = TextObject.text;

			if (a == null || a.Length == 0)
			{
				// Use the default placeholder if there is no text
				a = ((TextMeshProUGUI)TextObject.placeholder).text;
			}

			return a;
		}

		/// <summary>
		/// Set the current text value of a TMP_InputField
		/// </summary>
		/// <param name="TextArea">The GameObject that has TMP_InputField as a component</param>
		/// <param name="Text">The new text value</param>
		/// <returns>True on success, False on failure.</returns>
		private static bool SetInputTextField(GameObject TextArea, string Text)
		{
			TMP_InputField TextObject = TextArea.gameObject.GetComponent<TMP_InputField>();

			if (TextObject == null)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Warning, "SetTextFromInputField() called on GameObject with no TMP_InputField");
				return false;
			}

			TextObject.text = Text;
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"SetTextFromInputField({TextArea.name}, '{Text}') executed.");
			return true;
		}

		/// <summary>
		/// Event Handler when user clicked the "Direct Join" on menu selection
		/// </summary>
		private static void OnDirectJoinButtonClicked()
		{
			if (DirectConnectWindow == null)
			{
				CreateDirectConnectWindow();
			}

			DirectConnectWindow.SetActive(true);
		}

		/// <summary>
		/// Event Handler on server name input field change
		/// </summary>
		/// <param name="value">The new field value</param>
		private static void OnServerNameInputField_Changed(string value)
		{
			PublicServerJoinData.Address = value;

			if (!IsSyncingUIWithData) { PrivateServerJoinData = null; }
		}

		/// <summary>
		/// Event Handler on server port input field change
		/// </summary>
		/// <param name="value">The new field value</param>
		private static void OnServerPortInputField_Changed(string value)
		{
			// Trim the input to avoid leading/trailing spaces
			value = value.Trim();
			
			if (string.IsNullOrEmpty(value)) {
				PublicServerJoinData.ClearPort();
				return;
			}

			PublicServerJoinData.SetPort(value);
			if (!IsSyncingUIWithData) { PrivateServerJoinData = null; }
		}

		/// <summary>
		/// Event Handler on username input field change
		/// </summary>
		/// <param name="value">The new field value</param>
		private static void OnUsernameInputField_Changed(string value)
		{
			PublicServerJoinData.Username = value;
		}

		/// <summary>
		/// Event Handler when user clicked Connect on the Direct Join window
		/// </summary>
		private static void OnDirectJoinConnect()
		{
			DirectConnectWindow.SetActive(false);

			__MenuManager.SetLoadingScreen(true);

			// If there is no DNS-resolved IP, use the input field
			if (PrivateServerJoinData == null) {
				// Check if the Address input is empty (after trimming it)
				PublicServerJoinData.Address = PublicServerJoinData.Address.Trim();
				if (string.IsNullOrEmpty(PublicServerJoinData.Address))
				{
					LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, "Server IP/Hostname is empty");
					__MenuManager.SetLoadingScreen(false, Steamworks.RoomEnter.Error, "Server IP/Hostname is empty");
					return;
				}

				bool IsAHostname = ResolveDNS.IsOnHostnameFormat(PublicServerJoinData.Address);

				// Make sure the port in input is valid if we aren't auto configuring with DNS
				if (!IsAHostname && !PublicServerJoinData.IsPortValid)
				{
					// Invalid port
					__MenuManager.SetLoadingScreen(false, Steamworks.RoomEnter.Error, "Invalid Server Port, please check the input field");
					return;
				}

				// Check if CustomUsernamePatch is enabled
				if (LCDirectLan.GetConfig<bool>("Custom Username", "Enabled")) {
					if (PublicServerJoinData.Username.Length <= 0)
					{
						LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, $"Username too short, make sure the username is between 1 to {UsernameLengthLimit} ASCII characters");
						__MenuManager.SetLoadingScreen(false, Steamworks.RoomEnter.Error, "Username too short, make sure username is between 1 to 30 ASCII characters");
						return;
					}

					if (PublicServerJoinData.Username.Length > UsernameLengthLimit)
					{
						LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, $"Username too long, make sure the username is between 1 to {UsernameLengthLimit} ASCII characters");
						__MenuManager.SetLoadingScreen(false, Steamworks.RoomEnter.Error, "Username too long, make sure username is between 1 to 30 ASCII characters");
						return;
					}
				}

				PrivateServerJoinData = new PlayerJoinData(PublicServerJoinData.Address, PublicServerJoinData.Port, PublicServerJoinData.Username);

				LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, "DirectJoin Connect clicked !");
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, $"IP: '{PrivateServerJoinData.Address}'");
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, $"PORT: '{PrivateServerJoinData.Port}'");
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, $"Username: '{PrivateServerJoinData.Username}'");
			}

			// Check if the input looks like a valid hostname
			if (ResolveDNS.CheckIPType(PrivateServerJoinData.Address) == System.Net.Sockets.AddressFamily.Unknown && ResolveDNS.IsOnHostnameFormat(PrivateServerJoinData.Address)) {
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, "Detected a valid hostname, trying to resolve it...");

				__MenuManager.StartCoroutine(PerformDNSAutoConfigure(PrivateServerJoinData.Address, false, ResolvedData => {
					if (ResolvedData == null) {
						OnDirectJoinBack();
						__MenuManager.SetLoadingScreen(false, Steamworks.RoomEnter.Error, $"Unable to fetch server configuration data using DNS, make sure the hostname is correct and your DNS Provider supports it");
						return;
					}

					PrivateServerJoinData.Address = ResolvedData.Address;
					if (ResolvedData.IsPortValid) {
						PrivateServerJoinData.SetPort(ResolvedData.Port.ToString());
					}

					bool SyncNeeded = false;
					// Check if HideJoinData allows us to display the resolved IP
					if ((HideJoinData & 1) == 0) {
						PublicServerJoinData.Address = PrivateServerJoinData.Address;
						SyncNeeded = true;
					}

					// Check if HideJoinData allows us to display the resolved Port
					if ((HideJoinData & 2) == 0) {
						PublicServerJoinData.SetPort(PrivateServerJoinData.Port.ToString());
						SyncNeeded = true;
					}

					// Check if we should display the resolved IP to avoid server leak
					if (!SyncNeeded) {
						SyncUIWithPublicJoinData();
						LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"Synced UI to the resolved server data !");
					}

					ContinueDirectJoinConnect();
				}));

				return;
			}

			ContinueDirectJoinConnect();
		}

		/// <summary>
		/// Continue the direct join connect process (this is used to make ASYNC DNS resolve compatible with non-DNS connect)
		/// </summary>
		private static void ContinueDirectJoinConnect() {
			// Check if the input field or DNS resolved IP is not valid
			if (ResolveDNS.CheckIPType(PrivateServerJoinData.Address) == System.Net.Sockets.AddressFamily.Unknown)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, "Invalid Server IP/Hostname (final check)");
				__MenuManager.SetLoadingScreen(false, Steamworks.RoomEnter.Error, "Invalid Server IP/Hostname");
				return;
			}

			// Make sure the port is valid
			if (!PrivateServerJoinData.IsPortValid)
			{
				// Invalid port
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, "Invalid Server Port (final check)");
				__MenuManager.SetLoadingScreen(false, Steamworks.RoomEnter.Error, "Invalid Server Port");
				return;
			}

			// Check if CustomUsernamePatch is enabled
			if (LCDirectLan.GetConfig<bool>("Custom Username", "Enabled")) {
				// Save the username to runtime config, so other classes can get it
				LCDirectLan.SetConfig("Custom Username", "JoinDefaultUsername", PrivateServerJoinData.Username);
			}

			// If we should remember the current join information
			if (LCDirectLan.GetConfig<bool>("Join", "RememberLastJoinSettings"))
			{
				// Check if we should save the Join DefaultAddress
				if ((HideJoinData & 1) == 0)
				{
					LCDirectLan.SetConfig("Join", "DefaultAddress", PrivateServerJoinData.Address);
				}
				else {
					LCDirectLan.SetConfig("Join", "DefaultAddress", PublicServerJoinData.Address);
				}

				// Check if we should save the Join DefaultPort
				if ((HideJoinData & 2) == 0)
				{
					LCDirectLan.SetConfig("Join", "DefaultPort", PrivateServerJoinData.Port);
				}

				LCDirectLan.SaveConfig();
			}

			UnityTransport UTP = GameObject.Find("NetworkManager").GetComponent<UnityTransport>();
			UTP.ConnectionData.Address = PrivateServerJoinData.Address;
			UTP.ConnectionData.Port = PrivateServerJoinData.Port;

			LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, $"Connecting to {PrivateServerJoinData.Address}:{PrivateServerJoinData.Port}");
			
			// Check if we should not display the resolved IP to avoid server leak
			string LoadingText = "Connecting to ";

			// Should we show the IP Address?
			if ((HideJoinData & 1) == 0)
			{
				LoadingText += $"{PrivateServerJoinData.Address}";
			}
			else {
				LoadingText += "server";
			}

			// Should we show the Port Number?
			if ((HideJoinData & 2) == 0)
			{
				LoadingText += $":{PrivateServerJoinData.Port}";
			}

			SetLoadingText(LoadingText);

			GameObject.Find("MenuManager").GetComponent<MenuManager>().StartAClient();
			return;
		}
		
		/// <summary>
		/// Sync the UI with the public join data
		/// </summary>
		private static void SyncUIWithPublicJoinData()
		{
			GameObject ServerNameField = GameObject.Find("Canvas/MenuContainer/DirectConnectWindow/DCSettingsContainer/ServerNameField");
			GameObject ServerPortField = GameObject.Find("Canvas/MenuContainer/DirectConnectWindow/DCSettingsContainer/ServerPortField");
			GameObject UsernameField = GameObject.Find("Canvas/MenuContainer/DirectConnectWindow/DCSettingsContainer/CustomUsernameField");

			IsSyncingUIWithData = true;
			SetInputTextField(ServerNameField, PublicServerJoinData.Address);

			if (PublicServerJoinData.IsPortValid) {
				SetInputTextField(ServerPortField, PublicServerJoinData.Port.ToString());
			} else {
				SetInputTextField(ServerPortField, "");
			}
			
			if (UsernameField != null) {
				SetInputTextField(UsernameField, PublicServerJoinData.Username);
			}
			
			IsSyncingUIWithData = false;
		}

		/// <summary>
		/// Event Handler when user clicked back on the Direct Join window
		/// </summary>
		private static void OnDirectJoinBack()
		{
			if (DirectConnectWindow == null)
			{
				return;
			}

			DirectConnectWindow.SetActive(false);
		}

		/// <summary>
		/// Perform DNS Auto Configuration to resolve the server's IP and potentially Port
		/// </summary>
		/// <param name="RecordName">The DNS registered hostname to resolve</param>
		/// <param name="silent_resolve">Whether to not display loading animation with the UI</param>
		/// <param name="callback">The callback to execute after the DNS resolve is done</param>
		/// <returns>IEnumerator for Coroutine</returns>
		private static IEnumerator PerformDNSAutoConfigure(string RecordName, bool silent_resolve = false, Action<PlayerJoinData> callback = null)
		{
			PlayerJoinData ResolvedServerData = new PlayerJoinData(string.Empty, 0, string.Empty);

			if (!silent_resolve) {
				SetLoadingText("Trying to fetch configuration using DNS...");

				// Wait until the game updated the UI
				yield return new WaitForSeconds(0.25F);
			}

			// IPv4+Port
			#region SRV Resolve
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, $"Trying to DNS SRV resolve: {RecordName}");
			if (!silent_resolve) {
				SetLoadingText("Resolving target host with DNS (SRV)...");

				// Wait until the game updated the UI
				yield return new WaitForSeconds(0.25F);
			}

			(string, UInt16) SRVResolve = ResolveDNS.ResolveSRVRecord(RecordName);
			if (SRVResolve.Item1.Length > 0 || SRVResolve.Item2 > 0)
			{
				// Resolved using SRV
				ResolvedServerData.Address = SRVResolve.Item1;
				ResolvedServerData.SetPort(SRVResolve.Item2.ToString());

				LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, $"Resolved SRV for '{RecordName}' as: {ResolvedServerData.Address}:{ResolvedServerData.Port}");

				callback?.Invoke(ResolvedServerData);
				yield break;
			}
			#endregion

			// IPv4+Port Alternative
			#region TXT Resolve
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, $"Trying to use DNS TXT resolve: {RecordName}");
			if (!silent_resolve) {
				SetLoadingText("Resolving target host with DNS (TXT)...");

				// Wait until the game updated the UI
				yield return new WaitForSeconds(0.25F);
			}

			string TXTResolve = ResolveDNS.ResolveTXTRecord(RecordName);
			if (TXTResolve.Length > (LCDirectLan.PLUGIN_NAME.Length + 2))
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, $"Got TXT Data: {TXTResolve}");

				// If the TXT  Data starts with the plugin name and ":"
				if (TXTResolve.StartsWith(LCDirectLan.PLUGIN_NAME + "_"))
				{
					// Remove the prefix
					TXTResolve = TXTResolve.Substring(LCDirectLan.PLUGIN_NAME.Length + 1);

					int a = TXTResolve.IndexOf(":");

					// Potentially resolved using TXT
					if (a != -1)
					{
						string target_addr = TXTResolve.Substring(0, a);
						a++;

						if (ushort.TryParse(TXTResolve.Substring(a), out ushort port2))
						{
							// Resolved using TXT
							ResolvedServerData.Address = target_addr;
							ResolvedServerData.SetPort(port2.ToString());

							LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, $"Resolved TXT for '{RecordName}' as: {ResolvedServerData.Address}:{ResolvedServerData.Port}");
							
							callback?.Invoke(ResolvedServerData);
							yield break;
						}
					}
				}
				// TXT Failed, keep going down to another method.
			}
			#endregion

			// IPv6
			#region AAAA Resolve
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, $"Trying to use DNS AAAA resolve: {RecordName}");
			if (!silent_resolve) {
				SetLoadingText("Resolving target host with DNS (AAAA)...");

				// Wait until the game updated the UI
				yield return new WaitForSeconds(0.25F);
			}

			string AAAAResolve = ResolveDNS.ResolveAAAARecord(RecordName);

			if (AAAAResolve.Length > 0)
			{
				// Resolved using AAAA
				ResolvedServerData.Address = AAAAResolve;

				LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, $"Resolved AAAA for '{RecordName}' as: {ResolvedServerData.Address}");
				
				callback?.Invoke(ResolvedServerData);
				yield break;
			}
			#endregion

			// IPv4
			#region A Resolve
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, $"Trying to use DNS A resolve: {RecordName}");
			if (!silent_resolve) {
				SetLoadingText("Resolving target host with DNS (A)...");

				// Wait until the game updated the UI
				yield return new WaitForSeconds(0.25F);
			}

			string AResolve = ResolveDNS.ResolveARecord(RecordName);

			if (AResolve.Length > 0)
			{
				// Resolved using A
				ResolvedServerData.Address = AResolve;

				LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, $"Resolved A for '{RecordName}' as: {ResolvedServerData.Address}");
				
				callback?.Invoke(ResolvedServerData);
				yield break;
			}
			#endregion

			// Nothing, failed to auto configure
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, $"Unable to resolve anything for hostname '{RecordName}'");
			callback?.Invoke(null);
		}

		[HarmonyPatch("ClickHostButton")]
		[HarmonyPrefix]
		[HarmonyPriority(Priority.VeryLow)]
		public static void Prefix_ClickHostButton()
		{
			// Do not do anything when not in LAN mode
			if (!LCDirectLan.IsOnLanMode) { return; }

			GameObject.Find("NetworkManager").GetComponent<UnityTransport>().ConnectionData.Port = LCDirectLan.GetConfig<ushort>("Host", "DefaultPort");
		}

		[HarmonyPatch("StartHosting")]
		[HarmonyPrefix]
		[HarmonyPriority(Priority.VeryLow)]
		public static void Prefix_StartHosting()
		{
			// Do not do anything when not in LAN mode
			if (!LCDirectLan.IsOnLanMode) { return; }

			// Check if we should not listen on IPv6 instead of IPv4 which is the default
			if (!LCDirectLan.GetConfig<bool>("Host", "ListenOnIPv6")) {
				return;
			}

			UnityTransport a = GameObject.Find("NetworkManager").GetComponent<UnityTransport>();

			// Check if we should listen on localhost or any
			if (a.ConnectionData.ServerListenAddress == "127.0.0.1") {
				GameObject.Find("NetworkManager").GetComponent<UnityTransport>().ConnectionData.ServerListenAddress = "::1";
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, "Server Listen Address changed to IPv6 Localhost/Loopback (::1)");
				return;
			}

			GameObject.Find("NetworkManager").GetComponent<UnityTransport>().ConnectionData.ServerListenAddress = "::";
			LCDirectLan.Log(BepInEx.Logging.LogLevel.Debug, "Server Listen Address changed to IPv6 Any Address (::1)");
		}
		
		/// <summary>
		/// Utility function to change the game's LoadingText value while direct join is on process
		/// </summary>
		/// <param name="text">The new text to display</param>
		private static void SetLoadingText(string text)
		{
			if (string.IsNullOrEmpty(text)) { return; }

			GameObject a = GameObject.Find("Canvas/MenuContainer/LoadingScreen/LoadingTextContainer/LoadingText");
			TextMeshProUGUI b = a.GetComponent<TextMeshProUGUI>();
			b.text = text;
		}
	}
}
