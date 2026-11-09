using System;
using System.Globalization;
using System.Text;

namespace PaviseApp
{
	// One selectable outbound node. Tag is a synthetic ASCII name so the
	// generated mihomo config never has to quote free-form display names.
	internal sealed class NetNode
	{
		internal string Id;

		internal string Name;

		internal string Tag;

		internal string Type;

		internal string Server;

		internal int Port;

		internal string Cipher;

		internal string Password;

		internal string Uuid;

		internal string Flow;

		internal string Network;

		internal string Path;

		internal string HostHeader;

		internal string Sni;

		internal string PublicKey;

		internal string ShortId;

		internal string Alpn;

		internal string Obfs;

		internal string ObfsPassword;

		internal string Username;

		internal bool Tls;

		internal bool SkipCertVerify;

		internal int AlterId;

		internal string Region;

		internal bool Enabled = true;

		internal string Source;

		internal string Raw;

		internal string Endpoint
		{
			get
			{
				return (Server ?? "") + ":" + Port.ToString(CultureInfo.InvariantCulture);
			}
		}

		internal bool Usable
		{
			get
			{
				return Enabled && !string.IsNullOrEmpty(Type) && !string.IsNullOrEmpty(Server) && Port > 0 && Port <= 65535;
			}
		}

		internal string IdentityKey()
		{
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append(Type).Append('|').Append(Server).Append('|').Append(Port.ToString(CultureInfo.InvariantCulture)).Append('|');
			stringBuilder.Append(Password).Append('|').Append(Uuid).Append('|').Append(Username).Append('|').Append(Cipher);
			return stringBuilder.ToString();
		}

		internal string DisplayLabel()
		{
			string text = (Name ?? "").Trim();
			if (text.Length == 0)
			{
				text = Endpoint;
			}
			if (!string.IsNullOrEmpty(Region))
			{
				return text + " · " + Region;
			}
			return text;
		}

		internal NetNode Clone()
		{
			NetNode netNode = (NetNode)MemberwiseClone();
			return netNode;
		}

		internal static string DetectRegion(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return "";
			}
			string text = name.ToLowerInvariant();
			string[][] array = new string[17][]
			{
				new string[2] { "香港", "HK" },
				new string[2] { "hong kong", "HK" },
				new string[2] { "台湾", "TW" },
				new string[2] { "taiwan", "TW" },
				new string[2] { "日本", "JP" },
				new string[2] { "japan", "JP" },
				new string[2] { "新加坡", "SG" },
				new string[2] { "singapore", "SG" },
				new string[2] { "韩国", "KR" },
				new string[2] { "korea", "KR" },
				new string[2] { "美国", "US" },
				new string[2] { "united states", "US" },
				new string[2] { "德国", "DE" },
				new string[2] { "germany", "DE" },
				new string[2] { "英国", "GB" },
				new string[2] { "united kingdom", "GB" },
				new string[2] { "俄罗斯", "RU" }
			};
			string[][] array2 = array;
			foreach (string[] array3 in array2)
			{
				if (text.Contains(array3[0]))
				{
					return array3[1];
				}
			}
			string[] array4 = new string[20]
			{
				"hk", "tw", "jp", "sg", "kr", "us", "de", "gb", "uk", "ru",
				"ca", "au", "fr", "nl", "my", "th", "vn", "ph", "in", "tr"
			};
			string[] array5 = array4;
			foreach (string value in array5)
			{
				string text2 = "[" + value + "]";
				if (text.Contains(text2) || text.Contains(" " + value + " ") || text.StartsWith(value + "-") || text.StartsWith(value + "1") || text.StartsWith(value + "2") || text.Contains("_" + value) || text.Contains(value + "_"))
				{
					return value.ToUpperInvariant();
				}
			}
			return "";
		}
	}
}
