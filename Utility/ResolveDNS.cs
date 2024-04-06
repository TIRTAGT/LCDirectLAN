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
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using DnsClient;
using DnsClient.Protocol;

namespace LCDirectLAN.Utility
{
    internal class ResolveDNS
    {
		private static readonly Regex HostnameRuleMatch = new Regex("^(((([a-z0-9])|([a-z0-9](-|_)[a-z]))+\\.)+(([a-z0-9])|([a-z0-9](-|_)[a-z0-9]))+)$");

		/// <summary>
		/// Resolve a "A" (IPv4) Record
		/// </summary>
		/// <param name="record_name">The A record name to resolve</param>
		/// <returns>The IPv4 address as string, or empty string on failure</returns>
        public static string ResolveARecord(string record_name)
        {
			string result = string.Empty;

			LookupClient a = new LookupClient();
			IDnsQueryResponse b = null;

			try {
				b = a.Query(record_name, QueryType.A, QueryClass.IN);
			}
			catch(SocketException e)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, "Failed to resolve A Record: " + e.Message);
			}

			if (b == null) { return result; }
			if (b.HasError) { return result; }

			for (int i = 0; i < b.Answers.Count; i++)
			{
				if (b.Answers[i] == null || !(b.Answers[i] is ARecord)) { continue; }

				ARecord c = (ARecord)b.Answers[i];

				result = c.Address.ToString();
				break;
			}

			return result;
		}

		/// <summary>
		/// Resolve a "AAAA" (IPv6) Record
		/// </summary>
		/// <param name="record_name">The AAAA record name to resolve</param>
		/// <returns>The IPv6 address as string, or empty string on failure</returns>
        public static string ResolveAAAARecord(string record_name)
        {
			string result = string.Empty;

			LookupClient a = new LookupClient();
			IDnsQueryResponse b = null;

			try
			{
				b = a.Query(record_name, QueryType.AAAA, QueryClass.IN);
			}
			catch (SocketException e)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, "Failed to resolve AAAA Record: " + e.Message);
			}

			if (b == null) { return result; }
			if (b.HasError) { return result; }

			for (int i = 0; i < b.Answers.Count; i++)
			{
				if (b.Answers[i] == null || !(b.Answers[i] is AaaaRecord)) { continue; }

				AaaaRecord c = (AaaaRecord)b.Answers[i];

				result = c.Address.ToString();
				break;
			}

			return result;
		}

		/// <summary>
		/// Resolve a TXT Record
		/// </summary>
		/// <param name="record_name">The TXT record name to resolve</param>
		/// <returns>The first TXT data returned or empty when there is no data</returns>
        public static string ResolveTXTRecord(string record_name)
        {
            string result = string.Empty;

            LookupClient a = new LookupClient();
            IDnsQueryResponse b = null;

			try
			{
				b = a.Query(record_name, QueryType.TXT, QueryClass.IN);
			}
			catch (SocketException e)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, "Failed to resolve TXT Record: " + e.Message);
			}
            
			if (b == null) { return result; }
            if (b.HasError) { return result; }

            for (int i = 0; i < b.Answers.Count; i++)
            {
                if (b.Answers[i] == null || !(b.Answers[i] is TxtRecord)) {  continue; }

				TxtRecord c = (TxtRecord) b.Answers[i];

                foreach (string d in c.EscapedText)
                {
					result = d;
					break;
                }
                break;
            }

            return result;
        }

		/// <summary>
		/// Resolve a SRV Record
		/// </summary>
		/// <param name="record_name">The RSV record name to resolve</param>
		/// <returns>A tuple containing IPv4 Address as a string, and Port as a UInt16/ushort</returns>
		public static (string, UInt16) ResolveSRVRecord(string record_name)
		{
			(string, UInt16) result = (string.Empty, 0);

			LookupClient a = new LookupClient();
			IDnsQueryResponse b = null;

			try
			{
				b = a.Query(record_name, QueryType.SRV, QueryClass.IN);
			}
			catch (SocketException e)
			{
				LCDirectLan.Log(BepInEx.Logging.LogLevel.Error, "Failed to resolve SRV Record: " + e.Message);
				return result;
			}

			if (b == null) { return result; }
			if (b.HasError) { return result; }

			for (int i = 0; i < b.Answers.Count; i++)
			{
				if (b.Answers[i] == null || !(b.Answers[i] is SrvRecord)) { continue; }

				SrvRecord c = (SrvRecord)b.Answers[i];

				// Check if we should prioritize IPv6 lookup for the SRV Host
				if (LCDirectLan.GetConfig<bool>("Join", "SRVHost_PreferIPv6")) {
					// Try get host address via AAAA Record first
					result.Item1 = ResolveAAAARecord(c.Target.Value);

					if (string.IsNullOrEmpty(result.Item1))
					{
						// Try get host address using A Record as fallback
						result.Item1 = ResolveARecord(c.Target.Value);
						LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, "SRV Host is looked up using A Record");
					}
					else {
						LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, "SRV Host is looked up using AAAA Record");
					}

					result.Item2 = c.Port;
					break;
				}

				// Try get host address via A Record first
				result.Item1 = ResolveARecord(c.Target.Value);

				if (string.IsNullOrEmpty(result.Item1))
				{
					// Try get host address using AAAA Record as fallback
					result.Item1 = ResolveAAAARecord(c.Target.Value);
					LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, "SRV Host is looked up using AAAA Record");
				}
				else {
					LCDirectLan.Log(BepInEx.Logging.LogLevel.Info, "SRV Host is looked up using A Record");
				}

				result.Item2 = c.Port;
				break;
			}

			return result;
		}

		/// <summary>
		/// Check if a string is a valid IPv4 Address
		/// </summary>
		/// <param name="ip">The string to be checked</param>
		/// <returns>Boolean representing whether the string is a valid IPv4</returns>
		public static bool IsValidIPv4(string ip)
        {
            if (IPAddress.TryParse(ip, out IPAddress a))
			{
				return a.AddressFamily == AddressFamily.InterNetwork;
			}

			return false;
        }

		/// <summary>
		/// Check if a string is a valid IPv6 Address
		/// </summary>
		/// <param name="ip">The string to be checked</param>
		/// <returns>Boolean representing whether the string is a valid IPv6</returns>
		public static bool IsValidIPv6(string ip)
		{
			if (IPAddress.TryParse(ip, out IPAddress a))
			{
				return a.AddressFamily == AddressFamily.InterNetworkV6;
			}

			return false;
		}

		/// <summary>
		/// Check if a string is a valid IPv4/IPv6 Address
		/// </summary>
		/// <param name="ip">The string to be checked</param>
		/// <returns>An AddressFamily enum with:<br></br> - InterNetwork for IPv4<br></br> - InterNetworkV6 for IPv6<br></br> - Unknown otherwise.</returns>
		public static AddressFamily CheckIPType(string ip)
		{
			if (IsValidIPv4(ip)) { return AddressFamily.InterNetwork; }

			if (IsValidIPv6(ip)) { return AddressFamily.InterNetworkV6; }

			return AddressFamily.Unknown;
		}

		public static bool IsOnHostnameFormat(string hostname)
		{
			// If it's localhost, return true
			if (hostname == "localhost") { return true; }

			// If it's an IP, then it's not a hostname
			if (CheckIPType(hostname) != AddressFamily.Unknown) { return false; }
			
			return ResolveDNS.HostnameRuleMatch.IsMatch(hostname);
		}
    }
}
