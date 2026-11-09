using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PaviseApp
{
	// Picks the best node for one game. Selection is sticky: a challenger has to
	// beat the remembered node by a clear margin before the game is switched,
	// which keeps repeated probes from flapping the route mid-session.
	internal static class NetNodeSelector
	{
		internal const int SwitchMarginPercent = 18;

		internal sealed class Selection
		{
			internal NetNode Node;

			internal int Score;

			internal bool Sticky;

			internal string Reason;
		}

		private static readonly object gate = new object();

		private static string MemoryPath()
		{
			return Path.Combine(NetNodePool.CacheDir(), "selected.txt");
		}

		private static Dictionary<string, string> ReadMemory()
		{
			Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
			try
			{
				if (!File.Exists(MemoryPath()))
				{
					return dictionary;
				}
				string[] array = File.ReadAllLines(MemoryPath(), Encoding.UTF8);
				foreach (string text in array)
				{
					int num = text.IndexOf('|');
					if (num > 0)
					{
						dictionary[text.Substring(0, num)] = text.Substring(num + 1);
					}
				}
			}
			catch
			{
			}
			return dictionary;
		}

		internal static void Remember(string profileId, string nodeId)
		{
			if (string.IsNullOrEmpty(profileId) || string.IsNullOrEmpty(nodeId))
			{
				return;
			}
			lock (gate)
			{
				Dictionary<string, string> dictionary = ReadMemory();
				dictionary[profileId] = nodeId;
				List<string> list = new List<string>(dictionary.Count);
				foreach (KeyValuePair<string, string> item in dictionary)
				{
					list.Add(item.Key + "|" + item.Value);
				}
				try
				{
					AtomicFile.WriteLines(MemoryPath(), list.ToArray(), "netroute-selected");
				}
				catch
				{
				}
			}
		}

		internal static void Forget(string profileId)
		{
			lock (gate)
			{
				Dictionary<string, string> dictionary = ReadMemory();
				if (!dictionary.Remove(profileId))
				{
					return;
				}
				List<string> list = new List<string>(dictionary.Count);
				foreach (KeyValuePair<string, string> item in dictionary)
				{
					list.Add(item.Key + "|" + item.Value);
				}
				try
				{
					AtomicFile.WriteLines(MemoryPath(), list.ToArray(), "netroute-selected");
				}
				catch
				{
				}
			}
		}

		internal static Selection Choose(string profileId, List<NetNode> pool)
		{
			Selection selection = new Selection();
			if (pool == null || pool.Count == 0)
			{
				selection.Reason = Lang.T("netroute.select.empty");
				return selection;
			}
			Dictionary<string, string> dictionary = ReadMemory();
			string text;
			if (!string.IsNullOrEmpty(profileId) && dictionary.TryGetValue(profileId, out text))
			{
				foreach (NetNode node in pool)
				{
					if (node.Id != text)
					{
						continue;
					}
					NetNodeHealth.Entry entry = NetNodeHealth.Get(node.Id);
					if (entry == null || !entry.InCooldown())
					{
						selection.Node = node;
						selection.Score = entry == null ? 100000 : entry.Score();
						selection.Sticky = true;
						selection.Reason = Lang.T("netroute.select.sticky");
						return selection;
					}
					break;
				}
			}
			NetNode best = null;
			int num = int.MaxValue;
			NetNode bestCooling = null;
			int num2 = int.MaxValue;
			foreach (NetNode node2 in pool)
			{
				NetNodeHealth.Entry entry2 = NetNodeHealth.Get(node2.Id);
				int num3 = (entry2 == null) ? 100000 : entry2.Score();
				if (entry2 != null && entry2.InCooldown())
				{
					if (num3 < num2)
					{
						num2 = num3;
						bestCooling = node2;
					}
					continue;
				}
				if (num3 < num)
				{
					num = num3;
					best = node2;
				}
			}
			if (best == null)
			{
				best = bestCooling;
				num = num2;
			}
			if (best == null)
			{
				selection.Reason = Lang.T("netroute.select.empty");
				return selection;
			}
			// A remembered node that was only slightly worse keeps the route stable.
			if (!string.IsNullOrEmpty(profileId) && dictionary.ContainsKey(profileId))
			{
				NetNode next = NetNodePool.Find(dictionary[profileId]);
				if (next != null)
				{
					int num4 = NetNodeHealth.Score(next.Id);
					if (num4 > 0 && num4 < 100000 && num > 0 && num4 <= num * (100 + SwitchMarginPercent) / 100)
					{
						selection.Node = next;
						selection.Score = num4;
						selection.Sticky = true;
						selection.Reason = Lang.T("netroute.select.keep");
						return selection;
					}
				}
			}
			selection.Node = best;
			selection.Score = num;
			selection.Reason = Lang.T("netroute.select.best");
			return selection;
		}

		internal static string DescribeTop(List<NetNode> pool, int count)
		{
			if (pool == null || pool.Count == 0)
			{
				return "";
			}
			List<NetNode> list = new List<NetNode>(pool);
			list.Sort(delegate(NetNode a, NetNode b)
			{
				return NetNodeHealth.Score(a.Id).CompareTo(NetNodeHealth.Score(b.Id));
			});
			StringBuilder stringBuilder = new StringBuilder();
			int num = 0;
			foreach (NetNode node in list)
			{
				if (num >= count)
				{
					break;
				}
				NetNodeHealth.Entry entry = NetNodeHealth.Get(node.Id);
				string text = (entry == null || entry.LatencyMs <= 0) ? Lang.T("netroute.node.unprobed") : (entry.LatencyMs.ToString(CultureInfo.InvariantCulture) + " ms");
				if (stringBuilder.Length > 0)
				{
					stringBuilder.Append('\n');
				}
				stringBuilder.Append(node.DisplayLabel()).Append(" · ").Append(text);
				num++;
			}
			return stringBuilder.ToString();
		}
	}
}
