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
			IDnsQueryResponse b = a.Query(record_name, QueryType.A, QueryClass.IN);

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
		/// Resolve a TXT Record
		/// </summary>
		/// <param name="record_name">The TXT record name to resolve</param>
		/// <returns>The first TXT data returned or empty when there is no data</returns>
        public static string ResolveTXTRecord(string record_name)
        {
            string result = string.Empty;

            LookupClient a = new LookupClient();
            IDnsQueryResponse b = a.Query(record_name, QueryType.TXT, QueryClass.IN);
            
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
			IDnsQueryResponse b = a.Query(record_name, QueryType.SRV, QueryClass.IN);

			if (b.HasError) { return result; }

			for (int i = 0; i < b.Answers.Count; i++)
			{
				if (b.Answers[i] == null || !(b.Answers[i] is SrvRecord)) { continue; }

				SrvRecord c = (SrvRecord)b.Answers[i];

				// Get IP Address of the target hostname
				result.Item1 = ResolveDNS.ResolveARecord(c.Target.Value);
				result.Item2 = c.Port;
				break;
			}

			return result;
		}

		/// <summary>
		/// Check if a string is a valid IPv4 Address using RegEx
		/// </summary>
		/// <param name="ip">The string to be tested</param>
		/// <returns>Boolean representing whether the string is a valid IPv4</returns>
		public static bool IsValidIPv4(string ip)
        {
            if (IPAddress.TryParse(ip, out IPAddress a))
			{
				return a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
			}

			return false;
        }

		public static bool IsOnHostnameFormat(string hostname)
		{
			// If it's localhost, return true
			if (hostname == "localhost") { return true; }
			
			return ResolveDNS.HostnameRuleMatch.IsMatch(hostname);
		}
    }
}
