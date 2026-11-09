using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace PaviseApp
{
	// Node pool: bundled seed file (next to the exe), an editable local list in
	// the data directory, and cached subscription downloads.
	internal static class NetNodePool
	{
		internal const string SubscriptionsKey = "NetRouteSubscriptions";

		internal const string RefreshStampKey = "NetRouteRefreshStamp";

		internal const int MaxNodes = 96;

		internal const int RefreshHours = 6;

		private static readonly object gate = new object();

		private static List<NetNode> nodes = new List<NetNode>();

		private static long loadedStamp;

		private static string lastError = "";

		private static string StatusText = "";

		internal static string CacheDir()
		{
			string text = Path.Combine(Paths.Data, "netroute");
			try
			{
				Directory.CreateDirectory(text);
			}
			catch
			{
			}
			return text;
		}

		internal static string SeedPath()
		{
			try
			{
				string directoryName = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
				return Path.Combine(directoryName, "nodes.txt");
			}
			catch
			{
				return null;
			}
		}

		internal static string UserPath()
		{
			return Path.Combine(Paths.Data, "netnodes.txt");
		}

		private static string SubscriptionCachePath()
		{
			return Path.Combine(CacheDir(), "subscription.txt");
		}

		internal static string LastError()
		{
			lock (gate)
			{
				return lastError;
			}
		}

		internal static string Describe()
		{
			lock (gate)
			{
				return StatusText;
			}
		}

		internal static int Count()
		{
			lock (gate)
			{
				return nodes.Count;
			}
		}

		internal static List<NetNode> Snapshot()
		{
			lock (gate)
			{
				List<NetNode> list = new List<NetNode>(nodes.Count);
				foreach (NetNode node in nodes)
				{
					list.Add(node.Clone());
				}
				return list;
			}
		}

		internal static NetNode Find(string id)
		{
			lock (gate)
			{
				foreach (NetNode node in nodes)
				{
					if (node.Id == id)
					{
						return node.Clone();
					}
				}
			}
			return null;
		}

		// Rebuilds the in-memory pool at most once per thread-visible stamp.
		internal static List<NetNode> Load(bool force)
		{
			lock (gate)
			{
				long stamp = ComputeStamp();
				if (!force && loadedStamp == stamp && nodes.Count > 0)
				{
					List<NetNode> cached = new List<NetNode>(nodes.Count);
					foreach (NetNode node in nodes)
					{
						cached.Add(node.Clone());
					}
					return cached;
				}
				List<NetNode> list = new List<NetNode>();
				string text = "";
				ReadFile(SeedPath(), "seed", list, ref text);
				ReadFile(UserPath(), "local", list, ref text);
				ReadFile(SubscriptionCachePath(), "subscription", list, ref text);
				List<NetNode> list2 = Dedupe(list);
				if (list2.Count > MaxNodes)
				{
					list2.RemoveRange(MaxNodes, list2.Count - MaxNodes);
				}
				for (int i = 0; i < list2.Count; i++)
				{
					list2[i].Tag = "N" + (i + 1).ToString("000", CultureInfo.InvariantCulture);
				}
				nodes = list2;
				loadedStamp = stamp;
				lastError = text;
				StatusText = BuildStatusText(list2, text);
				List<NetNode> result = new List<NetNode>(nodes.Count);
				foreach (NetNode node2 in nodes)
				{
					result.Add(node2.Clone());
				}
				return result;
			}
		}

		private static string BuildStatusText(List<NetNode> list, string error)
		{
			int num = 0;
			int num2 = 0;
			int num3 = 0;
			foreach (NetNode node in list)
			{
				if (node.Source == "seed")
				{
					num++;
				}
				else if (node.Source == "local")
				{
					num2++;
				}
				else
				{
					num3++;
				}
			}
			string text = Lang.F("netroute.pool.count", list.Count, num, num2, num3);
			if ((error ?? "").Length > 0)
			{
				text = text + " · " + error;
			}
			return text;
		}

		private static long ComputeStamp()
		{
			long num = 0L;
			num = Math.Max(num, FileTicks(SeedPath()));
			num = Math.Max(num, FileTicks(UserPath()));
			num = Math.Max(num, FileTicks(SubscriptionCachePath()));
			num = Math.Max(num, Settings.LoadStr(SubscriptionsKey, "").GetHashCode());
			return num;
		}

		private static long FileTicks(string path)
		{
			try
			{
				if (string.IsNullOrEmpty(path) || !File.Exists(path))
				{
					return 0L;
				}
				return File.GetLastWriteTimeUtc(path).Ticks;
			}
			catch
			{
				return 0L;
			}
		}

		private static void ReadFile(string path, string source, List<NetNode> sink, ref string error)
		{
			if (string.IsNullOrEmpty(path))
			{
				return;
			}
			try
			{
				if (!File.Exists(path))
				{
					return;
				}
				string text = File.ReadAllText(path, Encoding.UTF8);
				List<NetNode> list = NetNodeParser.ParseText(text, source);
				sink.AddRange(list);
			}
			catch (Exception error2)
			{
				error = source + ": " + error2.GetType().Name;
				Logger.Warn("net pool read failed (" + source + "): " + error2.Message);
			}
		}

		private static List<NetNode> Dedupe(List<NetNode> input)
		{
			List<NetNode> list = new List<NetNode>(input.Count);
			HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
			foreach (NetNode node in input)
			{
				if (node == null || !node.Usable || node.Id == null)
				{
					continue;
				}
				if (hashSet.Add(node.Id))
				{
					list.Add(node);
				}
			}
			return list;
		}

		internal static string SubscriptionFilePath()
		{
			return Path.Combine(Paths.Data, "netnodes.subscriptions.txt");
		}

		internal static int SubscriptionUrls(out List<string> urls)
		{
			urls = new List<string>();
			string text = Settings.LoadStr(SubscriptionsKey, "");
			try
			{
				string text2 = SubscriptionFilePath();
				if (File.Exists(text2))
				{
					text = text + "\n" + File.ReadAllText(text2, Encoding.UTF8);
				}
			}
			catch
			{
			}
			if (string.IsNullOrEmpty(text))
			{
				return 0;
			}
			string[] array = text.Replace("\r", "").Split(new char[2] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries);
			string[] array2 = array;
			foreach (string text2 in array2)
			{
				string text3 = text2.Trim();
				if (text3.Length >= 8 && (text3.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text3.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
				{
					urls.Add(text3);
				}
			}
			return urls.Count;
		}

		internal static bool DueForRefresh()
		{
			long num = 0L;
			long.TryParse(Settings.LoadStr(RefreshStampKey, "0"), out num);
			long utcNow = DateTime.UtcNow.Ticks;
			return utcNow - num >= TimeSpan.FromHours(6.0).Ticks;
		}

		// Downloads every configured subscription into one cache file. Returns the
		// number of nodes parsed from the refreshed content.
		internal static int RefreshSubscriptions(int perRequestTimeoutMs, out string error)
		{
			error = "";
			List<string> urls;
			if (SubscriptionUrls(out urls) == 0)
			{
				error = Lang.T("netroute.sub.none");
				return 0;
			}
			List<string> list = new List<string>();
			int num = 0;
			foreach (string url in urls)
			{
				string text;
				string text2;
				if (!Fetch(url, perRequestTimeoutMs, out text, out text2))
				{
					error = text2;
					Logger.Warn("subscription fetch failed: " + text2);
					continue;
				}
				List<NetNode> list2 = NetNodeParser.ParseText(text, "subscription");
				Logger.Info("subscription parsed " + list2.Count.ToString(CultureInfo.InvariantCulture) + " nodes from " + url);
				if (list2.Count == 0)
				{
					continue;
				}
				num += list2.Count;
				// Cache the publisher's own payload so a later load parses it
				// exactly the same way the download did.
				string[] array = text.Replace("\r", "").Split(new char[1] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
				list.AddRange(array);
				list.Add("# pavise-subscription-break");
			}
			if (num == 0 || list.Count == 0)
			{
				return 0;
			}
			bool flag = AtomicFile.WriteLines(SubscriptionCachePath(), list.ToArray(), "netroute-subscription");
			if (!flag)
			{
				error = Lang.T("netroute.sub.writefail");
				return 0;
			}
			Settings.SaveStr(RefreshStampKey, DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
			Load(true);
			Logger.Log(Lang.F("netroute.sub.refreshed", list.Count, Count()));
			return list.Count;
		}

		private static bool Fetch(string url, int timeoutMs, out string text, out string error)
		{
			text = null;
			error = "";
			try
			{
				ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
			HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(url);
				httpWebRequest.Timeout = Math.Max(1000, timeoutMs);
				httpWebRequest.ReadWriteTimeout = Math.Max(1000, timeoutMs);
				httpWebRequest.UserAgent = "Pavise/" + Logger.Version;
				httpWebRequest.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
				httpWebRequest.Proxy = null;
				using (HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse())
				{
					if (httpWebResponse.StatusCode != HttpStatusCode.OK)
					{
						error = url + " -> " + (int)httpWebResponse.StatusCode;
						return false;
					}
					using (Stream responseStream = httpWebResponse.GetResponseStream())
					{
						if (responseStream == null)
						{
							error = url + " -> empty";
							return false;
						}
						using (StreamReader streamReader = new StreamReader(responseStream, Encoding.UTF8))
						{
							text = streamReader.ReadToEnd();
						}
					}
				}
				return text != null && text.Length > 0;
			}
			catch (Exception ex)
			{
				error = url + " -> " + ex.Message;
				return false;
			}
		}

		internal static void EnsureLoaded()
		{
			if (Count() == 0)
			{
				Load(false);
			}
		}

		internal static void Wake()
		{
			lock (gate)
			{
				loadedStamp = 0L;
			}
		}
	}
}
