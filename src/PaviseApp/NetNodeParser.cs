using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PaviseApp
{
	// Parses the share links that free-node pools publish (ss / vmess / vless /
	// trojan / hysteria2 / socks5 / http) plus a Clash-style `proxies:` block.
	internal static class NetNodeParser
	{
		internal static List<NetNode> ParseText(string text, string source)
		{
			List<NetNode> list = new List<NetNode>();
			if (string.IsNullOrEmpty(text))
			{
				return list;
			}
			string text2 = text.Replace("\r", "");
			if (text2.IndexOf("proxies:", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				ParseClash(text2, source, list);
				return list;
			}
			string text3 = text2;
			if (text3.IndexOf("://", StringComparison.Ordinal) < 0)
			{
				string text4 = Base64Decode(text3.Trim());
				if (text4 != null && text4.IndexOf("://", StringComparison.Ordinal) >= 0)
				{
					text3 = text4;
				}
			}
			string[] array = text3.Split(new char[2] { '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries);
			string[] array2 = array;
			foreach (string text5 in array2)
			{
				NetNode netNode = ParseLine(text5);
				if (netNode != null)
				{
					netNode.Source = source;
					netNode.Raw = text5;
					list.Add(netNode);
				}
			}
			return list;
		}

		internal static NetNode ParseLine(string line)
		{
			if (string.IsNullOrEmpty(line))
			{
				return null;
			}
			string text = line.Trim();
			try
			{
				if (text.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase))
				{
					return ParseVmess(text.Substring(8));
				}
				if (text.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
				{
					return ParseVless(text.Substring(8));
				}
				if (text.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase))
				{
					return ParseTrojan(text.Substring(9));
				}
				if (text.StartsWith("ss://", StringComparison.OrdinalIgnoreCase))
				{
					return ParseShadowsocks(text.Substring(5));
				}
				if (text.StartsWith("hysteria2://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("hy2://", StringComparison.OrdinalIgnoreCase))
				{
					int num = text.IndexOf("://", StringComparison.Ordinal);
					return ParseHysteria2(text.Substring(num + 3));
				}
				if (text.StartsWith("socks5://", StringComparison.OrdinalIgnoreCase))
				{
					return ParseSocks(text.Substring(9), "socks5", false);
				}
				if (text.StartsWith("socks://", StringComparison.OrdinalIgnoreCase))
				{
					return ParseSocks(text.Substring(8), "socks5", false);
				}
				if (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
				{
					return ParseSocks(text.Substring(8), "http", true);
				}
				if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
				{
					return ParseSocks(text.Substring(7), "http", false);
				}
			}
			catch
			{
				return null;
			}
			return null;
		}

		private static NetNode Finish(NetNode node)
		{
			if (node == null || string.IsNullOrEmpty(node.Server) || node.Port <= 0 || node.Port > 65535)
			{
				return null;
			}
			if (node.Name == null || node.Name.Trim().Length == 0)
			{
				node.Name = node.Server + ":" + node.Port.ToString(CultureInfo.InvariantCulture);
			}
			node.Name = node.Name.Trim();
			node.Region = NetNode.DetectRegion(node.Name);
			node.Id = Hash(node.IdentityKey());
			return node;
		}

		internal static string Hash(string value)
		{
			ulong num = 1469598103934665603uL;
			for (int i = 0; i < value.Length; i++)
			{
				num ^= (ulong)value[i];
				num *= 1099511628211uL;
			}
			return num.ToString("x16", CultureInfo.InvariantCulture);
		}

		private static NetNode ParseShadowsocks(string body)
		{
			string text = body;
			string text2 = "";
			int num = text.IndexOf('#');
			if (num >= 0)
			{
				text2 = Uri.UnescapeDataString(text.Substring(num + 1));
				text = text.Substring(0, num);
			}
			string query = "";
			int num2 = text.IndexOf('?');
			if (num2 >= 0)
			{
				query = text.Substring(num2 + 1);
				text = text.Substring(0, num2);
			}
			if (text.IndexOf('@') < 0)
			{
				string text3 = Base64Decode(Uri.UnescapeDataString(text.TrimEnd('/')));
				if (text3 == null)
				{
					return null;
				}
				text = text3;
			}
			int num3 = text.LastIndexOf('@');
			if (num3 <= 0)
			{
				return null;
			}
			string text4 = text.Substring(0, num3);
			string text5 = text.Substring(num3 + 1);
			string text6 = Base64Decode(text4);
			if (text6 == null)
			{
				text6 = Uri.UnescapeDataString(text4);
			}
			int num4 = text6.IndexOf(':');
			if (num4 <= 0)
			{
				return null;
			}
			string cipher = text6.Substring(0, num4);
			string password = text6.Substring(num4 + 1);
			NetNode netNode = new NetNode();
			netNode.Type = "ss";
			netNode.Cipher = cipher.Trim();
			netNode.Password = password;
			if (!SplitHostPort(text5, netNode))
			{
				return null;
			}
			netNode.Name = text2;
			if (query.Length > 0)
			{
				Dictionary<string, string> dictionary = ParseQuery(query);
				string value;
				if (dictionary.TryGetValue("plugin", out value))
				{
					// obfs plugins are not carried into the generated core config.
					netNode.Obfs = value;
				}
			}
			return Finish(netNode);
		}

		private static NetNode ParseVmess(string payload)
		{
			string text = payload;
			string text2 = Base64Decode(text);
			if (text2 == null)
			{
				return null;
			}
			Dictionary<string, string> dictionary = ParseFlatJson(text2);
			if (dictionary == null)
			{
				return null;
			}
			string value;
			NetNode netNode = new NetNode();
			netNode.Type = "vmess";
			netNode.Server = dictionary["add"];
			netNode.Port = ToInt(dictionary, "port");
			netNode.Uuid = dictionary["id"];
			netNode.AlterId = ToInt(dictionary, "aid");
			if (netNode.AlterId <= 0)
			{
				netNode.AlterId = 0;
			}
			netNode.Cipher = (dictionary.TryGetValue("scy", out value) && value.Length > 0) ? value : "auto";
			netNode.Network = (dictionary.TryGetValue("net", out value) && value.Length > 0) ? value : "tcp";
			if (dictionary.TryGetValue("path", out value))
			{
				netNode.Path = value;
			}
			if (dictionary.TryGetValue("host", out value) && value.Length > 0)
			{
				netNode.HostHeader = value;
			}
			if (dictionary.TryGetValue("sni", out value) && value.Length > 0)
			{
				netNode.Sni = value;
			}
			netNode.Tls = dictionary.TryGetValue("tls", out value) && (value == "tls" || value == "1" || value == "true");
			if (dictionary.TryGetValue("alpn", out value) && value.Length > 0)
			{
				netNode.Alpn = value;
			}
			if (dictionary.TryGetValue("ps", out value))
			{
				netNode.Name = value;
			}
			return Finish(netNode);
		}

		private static NetNode ParseVless(string body)
		{
			string text = body;
			string text2 = "";
			int num = text.IndexOf('#');
			if (num >= 0)
			{
				text2 = Uri.UnescapeDataString(text.Substring(num + 1));
				text = text.Substring(0, num);
			}
			string query = "";
			int num2 = text.IndexOf('?');
			if (num2 >= 0)
			{
				query = text.Substring(num2 + 1);
				text = text.Substring(0, num2);
			}
			int num3 = text.LastIndexOf('@');
			if (num3 <= 0)
			{
				return null;
			}
			NetNode netNode = new NetNode();
			netNode.Type = "vless";
			netNode.Uuid = Uri.UnescapeDataString(text.Substring(0, num3));
			if (!SplitHostPort(text.Substring(num3 + 1), netNode))
			{
				return null;
			}
			netNode.Name = text2;
			Dictionary<string, string> dictionary = ParseQuery(query);
			string value;
			if (dictionary.TryGetValue("type", out value) && value.Length > 0)
			{
				netNode.Network = value;
			}
			if (dictionary.TryGetValue("security", out value))
			{
				netNode.Tls = value == "tls" || value == "reality";
				if (value == "reality")
				{
					netNode.PublicKey = Get(dictionary, "pbk");
					netNode.ShortId = Get(dictionary, "sid");
				}
				if (value == "reality")
				{
					netNode.Sni = Get(dictionary, "sni");
				}
			}
			if (dictionary.TryGetValue("sni", out value) && value.Length > 0)
			{
				netNode.Sni = value;
			}
			if (dictionary.TryGetValue("flow", out value) && value.Length > 0)
			{
				netNode.Flow = value;
			}
			if (dictionary.TryGetValue("path", out value) && value.Length > 0)
			{
				netNode.Path = value;
			}
			if (dictionary.TryGetValue("host", out value) && value.Length > 0)
			{
				netNode.HostHeader = value;
			}
			if (dictionary.TryGetValue("allowInsecure", out value) || dictionary.TryGetValue("insecure", out value))
			{
				netNode.SkipCertVerify = value == "1" || value == "true";
			}
			return Finish(netNode);
		}

		private static NetNode ParseTrojan(string body)
		{
			string text = body;
			string text2 = "";
			int num = text.IndexOf('#');
			if (num >= 0)
			{
				text2 = Uri.UnescapeDataString(text.Substring(num + 1));
				text = text.Substring(0, num);
			}
			string query = "";
			int num2 = text.IndexOf('?');
			if (num2 >= 0)
			{
				query = text.Substring(num2 + 1);
				text = text.Substring(0, num2);
			}
			int num3 = text.LastIndexOf('@');
			if (num3 <= 0)
			{
				return null;
			}
			NetNode netNode = new NetNode();
			netNode.Type = "trojan";
			netNode.Password = Uri.UnescapeDataString(text.Substring(0, num3));
			netNode.Tls = true;
			if (!SplitHostPort(text.Substring(num3 + 1), netNode))
			{
				return null;
			}
			netNode.Name = text2;
			Dictionary<string, string> dictionary = ParseQuery(query);
			string value;
			if (dictionary.TryGetValue("sni", out value) || dictionary.TryGetValue("peer", out value))
			{
				netNode.Sni = value;
			}
			if (dictionary.TryGetValue("allowInsecure", out value) || dictionary.TryGetValue("insecure", out value))
			{
				netNode.SkipCertVerify = value == "1" || value == "true";
			}
			if (dictionary.TryGetValue("type", out value) && (value == "ws" || value == "grpc"))
			{
				netNode.Network = value;
			}
			if (dictionary.TryGetValue("path", out value) && value.Length > 0)
			{
				netNode.Path = value;
			}
			if (dictionary.TryGetValue("host", out value) && value.Length > 0)
			{
				netNode.HostHeader = value;
			}
			return Finish(netNode);
		}

		private static NetNode ParseHysteria2(string body)
		{
			string text = body;
			string text2 = "";
			int num = text.IndexOf('#');
			if (num >= 0)
			{
				text2 = Uri.UnescapeDataString(text.Substring(num + 1));
				text = text.Substring(0, num);
			}
			string query = "";
			int num2 = text.IndexOf('?');
			if (num2 >= 0)
			{
				query = text.Substring(num2 + 1);
				text = text.Substring(0, num2);
			}
			NetNode netNode = new NetNode();
			netNode.Type = "hysteria2";
			int num3 = text.LastIndexOf('@');
			if (num3 > 0)
			{
				netNode.Password = Uri.UnescapeDataString(text.Substring(0, num3));
				text = text.Substring(num3 + 1);
			}
			if (!SplitHostPort(text, netNode))
			{
				return null;
			}
			netNode.Name = text2;
			Dictionary<string, string> dictionary = ParseQuery(query);
			string value;
			if (dictionary.TryGetValue("sni", out value) && value.Length > 0)
			{
				netNode.Sni = value;
			}
			if (dictionary.TryGetValue("insecure", out value) || dictionary.TryGetValue("allowInsecure", out value))
			{
				netNode.SkipCertVerify = value == "1" || value == "true";
			}
			if (dictionary.TryGetValue("obfs", out value) && value.Length > 0)
			{
				netNode.Obfs = value;
			}
			if (dictionary.TryGetValue("obfs-password", out value) && value.Length > 0)
			{
				netNode.ObfsPassword = value;
			}
			return Finish(netNode);
		}

		private static NetNode ParseSocks(string body, string type, bool tls)
		{
			string text = body;
			string text2 = "";
			int num = text.IndexOf('#');
			if (num >= 0)
			{
				text2 = Uri.UnescapeDataString(text.Substring(num + 1));
				text = text.Substring(0, num);
			}
			int num2 = text.IndexOf('?');
			if (num2 >= 0)
			{
				text = text.Substring(0, num2);
			}
			NetNode netNode = new NetNode();
			netNode.Type = type;
			netNode.Tls = tls;
			int num3 = text.LastIndexOf('@');
			if (num3 > 0)
			{
				string text3 = Uri.UnescapeDataString(text.Substring(0, num3));
				int num4 = text3.IndexOf(':');
				if (num4 > 0)
				{
					netNode.Username = text3.Substring(0, num4);
					netNode.Password = text3.Substring(num4 + 1);
				}
				else
				{
					netNode.Username = text3;
				}
				text = text.Substring(num3 + 1);
			}
			if (!SplitHostPort(text, netNode))
			{
				return null;
			}
			netNode.Name = text2;
			return Finish(netNode);
		}

		private static bool SplitHostPort(string value, NetNode node)
		{
			string text = value.Trim();
			if (text.Length == 0)
			{
				return false;
			}
			if (text.StartsWith("["))
			{
				int num = text.IndexOf(']');
				if (num < 0)
				{
					return false;
				}
				node.Server = text.Substring(1, num - 1);
				if (num + 1 >= text.Length || text[num + 1] != ':')
				{
					return false;
				}
				node.Port = ParsePort(text.Substring(num + 2));
				return node.Port > 0;
			}
			int num2 = text.LastIndexOf(':');
			if (num2 <= 0)
			{
				return false;
			}
			node.Server = text.Substring(0, num2);
			node.Port = ParsePort(text.Substring(num2 + 1));
			return node.Port > 0;
		}

		private static int ParsePort(string value)
		{
			string text = value.Trim();
			int num = text.IndexOfAny(new char[2] { '/', '?' });
			if (num >= 0)
			{
				text = text.Substring(0, num);
			}
			int result;
			if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) && result > 0 && result <= 65535)
			{
				return result;
			}
			return 0;
		}

		private static int ToInt(Dictionary<string, string> map, string key)
		{
			string text;
			if (!map.TryGetValue(key, out text))
			{
				return 0;
			}
			int result;
			if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
			{
				return 0;
			}
			return result;
		}

		private static string Get(Dictionary<string, string> map, string key)
		{
			string value;
			if (!map.TryGetValue(key, out value))
			{
				return "";
			}
			return value;
		}

		internal static Dictionary<string, string> ParseQuery(string query)
		{
			Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			if (string.IsNullOrEmpty(query))
			{
				return dictionary;
			}
			string[] array = query.Split(new char[1] { '&' }, StringSplitOptions.RemoveEmptyEntries);
			string[] array2 = array;
			foreach (string text in array2)
			{
				int num = text.IndexOf('=');
				if (num > 0)
				{
					dictionary[Uri.UnescapeDataString(text.Substring(0, num))] = Uri.UnescapeDataString(text.Substring(num + 1));
				}
				else if (text.Length > 0)
				{
					dictionary[text] = "";
				}
			}
			return dictionary;
		}

		// Flat JSON reader for the vmess payload: values are only ever strings,
		// numbers or booleans, so a key/value scan is sufficient.
		private static Dictionary<string, string> ParseFlatJson(string json)
		{
			Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			int num = 0;
			int length = json.Length;
			while (num < length)
			{
				int num2 = json.IndexOf('"', num);
				if (num2 < 0)
				{
					break;
				}
				string text;
				num = ReadJsonString(json, num2, out text);
				if (num < 0)
				{
					break;
				}
				int num3 = json.IndexOf(':', num);
				if (num3 < 0)
				{
					break;
				}
				int num4 = num3 + 1;
				while (num4 < length && char.IsWhiteSpace(json[num4]))
				{
					num4++;
				}
				if (num4 >= length)
				{
					break;
				}
				char c = json[num4];
				if (c == '"')
				{
					string text2;
					num = ReadJsonString(json, num4, out text2);
					if (num < 0)
					{
						break;
					}
					dictionary[text] = text2;
					continue;
				}
				int num5 = num4;
				while (num5 < length && json[num5] != ',' && json[num5] != '}' && json[num5] != ']')
				{
					num5++;
				}
				string text3 = json.Substring(num4, num5 - num4).Trim();
				dictionary[text] = text3;
				num = num5;
			}
			return (dictionary.Count > 0) ? dictionary : null;
		}

		private static int ReadJsonString(string json, int quoteAt, out string value)
		{
			value = null;
			StringBuilder stringBuilder = new StringBuilder();
			int i = quoteAt + 1;
			while (i < json.Length)
			{
				char c = json[i];
				if (c == '\\')
				{
					i++;
					if (i >= json.Length)
					{
						return -1;
					}
					char c2 = json[i];
					switch (c2)
					{
					case 'n':
						stringBuilder.Append('\n');
						break;
					case 'r':
						stringBuilder.Append('\r');
						break;
					case 't':
						stringBuilder.Append('\t');
						break;
					case 'u':
						if (i + 4 >= json.Length)
						{
							return -1;
						}
						try
						{
							stringBuilder.Append((char)Convert.ToInt32(json.Substring(i + 1, 4), 16));
						}
						catch
						{
							return -1;
						}
						i += 4;
						break;
					default:
						stringBuilder.Append(c2);
						break;
					}
					i++;
					continue;
				}
				if (c == '"')
				{
					value = stringBuilder.ToString();
					return i + 1;
				}
				stringBuilder.Append(c);
				i++;
			}
			return -1;
		}

		internal static string Base64Decode(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return null;
			}
			string text = value.Trim();
			int num = text.IndexOf('%');
			if (num >= 0)
			{
				text = Uri.UnescapeDataString(text);
			}
			text = text.Replace('-', '+').Replace('_', '/').Replace("\r", "").Replace("\n", "").Replace(" ", "");
			int num2 = text.IndexOf('=');
			if (num2 >= 0)
			{
				text = text.Substring(0, num2);
			}
			int num3 = text.Length % 4;
			if (num3 == 1)
			{
				return null;
			}
			if (num3 > 0)
			{
				text = text + new string('=', 4 - num3);
			}
			try
			{
				byte[] array = Convert.FromBase64String(text);
				return Encoding.UTF8.GetString(array);
			}
			catch
			{
				return null;
			}
		}

		// Minimal indentation-aware reader for a Clash `proxies:` block.
		private static void ParseClash(string text, string source, List<NetNode> output)
		{
			string[] array = text.Split(new char[1] { '\n' }, StringSplitOptions.None);
			bool flag = false;
			int num = -1;
			Dictionary<string, string> dictionary = null;
			List<KeyValuePair<int, string>> list = new List<KeyValuePair<int, string>>(4);
			foreach (string text2 in array)
			{
				string text3 = text2.TrimEnd();
				if (text3.Length == 0)
				{
					continue;
				}
				if (text3.TrimStart().StartsWith("#"))
				{
					continue;
				}
				int num2 = text3.Length - text3.TrimStart().Length;
				string text4 = text3.TrimStart();
				if (!flag)
				{
					if (text4.Equals("proxies:", StringComparison.OrdinalIgnoreCase) || text4.StartsWith("proxies:"))
					{
						flag = true;
						num = num2;
					}
					continue;
				}
				if (num2 <= num)
				{
					FlushClash(dictionary, output, source);
					break;
				}
				if (text4.StartsWith("- "))
				{
					FlushClash(dictionary, output, source);
					dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
					list.Clear();
					text4 = text4.Substring(2).Trim();
				}
				if (dictionary == null)
				{
					continue;
				}
				AddClashLine(dictionary, list, num2, text4);
			}
			FlushClash(dictionary, output, source);
		}

		private static void AddClashLine(Dictionary<string, string> map, List<KeyValuePair<int, string>> path, int indent, string content)
		{
			int num = content.IndexOf(':');
			if (num <= 0)
			{
				return;
			}
			string text = Unquote(content.Substring(0, num).Trim());
			string text2 = content.Substring(num + 1).Trim();
			while (path.Count > 0 && path[path.Count - 1].Key >= indent)
			{
				path.RemoveAt(path.Count - 1);
			}
			StringBuilder stringBuilder = new StringBuilder();
			foreach (KeyValuePair<int, string> item in path)
			{
				stringBuilder.Append(item.Value).Append('.');
			}
			if (text2.Length == 0)
			{
				path.Add(new KeyValuePair<int, string>(indent, text));
				return;
			}
			stringBuilder.Append(text);
			text2 = Unquote(text2);
			if (text2.StartsWith("[") && text2.EndsWith("]"))
			{
				text2 = text2.Substring(1, text2.Length - 2).Trim();
			}
			map[stringBuilder.ToString()] = text2;
		}

		private static string Unquote(string value)
		{
			string text = value.Trim();
			if (text.Length >= 2 && ((text[0] == '"' && text[text.Length - 1] == '"') || (text[0] == '\'' && text[text.Length - 1] == '\'')))
			{
				text = text.Substring(1, text.Length - 2);
			}
			return text;
		}

		private static void FlushClash(Dictionary<string, string> map, List<NetNode> output, string source)
		{
			if (map == null || map.Count == 0)
			{
				return;
			}
			NetNode netNode = FromClashMap(map);
			if (netNode != null)
			{
				netNode.Source = source;
				output.Add(netNode);
			}
		}

		private static NetNode FromClashMap(Dictionary<string, string> map)
		{
			string type = map.ContainsKey("type") ? map["type"].Trim().ToLowerInvariant() : "";
			if (type.Length == 0)
			{
				return null;
			}
			if (type == "ssr" || type == "snell" || type == "wireguard")
			{
				return null;
			}
			NetNode netNode = new NetNode();
			netNode.Type = (type == "hy2") ? "hysteria2" : type;
			netNode.Name = (map.ContainsKey("name") ? map["name"] : "");
			netNode.Server = (map.ContainsKey("server") ? map["server"] : "");
			int num;
			if (int.TryParse(map.ContainsKey("port") ? map["port"] : "", NumberStyles.Integer, CultureInfo.InvariantCulture, out num))
			{
				netNode.Port = num;
			}
			netNode.Cipher = (map.ContainsKey("cipher") ? map["cipher"] : "");
			netNode.Password = (map.ContainsKey("password") ? map["password"] : "");
			netNode.Uuid = (map.ContainsKey("uuid") ? map["uuid"] : "");
			netNode.Flow = (map.ContainsKey("flow") ? map["flow"] : "");
			netNode.Network = (map.ContainsKey("network") ? map["network"] : "");
			netNode.Path = First(map, "ws-opts.path", "grpc-opts.grpc-service-name", "path");
			netNode.HostHeader = First(map, "ws-opts.headers.Host", "ws-opts.headers.host", "host");
			netNode.Sni = First(map, "servername", "sni");
			netNode.PublicKey = First(map, "reality-opts.public-key");
			netNode.ShortId = First(map, "reality-opts.short-id");
			netNode.Alpn = First(map, "alpn");
			netNode.Obfs = First(map, "obfs", "obfs-opts.mode");
			netNode.ObfsPassword = First(map, "obfs-password", "obfs-opts.password");
			netNode.Username = (map.ContainsKey("username") ? map["username"] : "");
			string text;
			netNode.Tls = map.ContainsKey("tls") && (map["tls"] == "true" || map["tls"] == "1");
			if (map.TryGetValue("skip-cert-verify", out text))
			{
				netNode.SkipCertVerify = text == "true" || text == "1";
			}
			if (map.TryGetValue("alterId", out text))
			{
				int num2;
				if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out num2))
				{
					netNode.AlterId = num2;
				}
			}
			if (netNode.Type == "vmess" && string.IsNullOrEmpty(netNode.Cipher))
			{
				netNode.Cipher = "auto";
			}
			if (netNode.Type == "trojan")
			{
				netNode.Tls = true;
			}
			if (netNode.Type == "vless" && (netNode.PublicKey ?? "").Length > 0)
			{
				netNode.Tls = true;
			}
			return Finish(netNode);
		}

		private static string First(Dictionary<string, string> map, params string[] keys)
		{
			foreach (string key in keys)
			{
				string value;
				if (map.TryGetValue(key, out value) && value.Length > 0)
				{
					return value;
				}
			}
			return "";
		}
	}
}
