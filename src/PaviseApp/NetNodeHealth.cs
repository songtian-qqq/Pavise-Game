using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PaviseApp
{
	// Per-node probe history: latency, jitter, failures and the time of the last
	// measurement. Kept as a flat text file so a corrupt line can never poison
	// the whole pool.
	internal static class NetNodeHealth
	{
		internal sealed class Entry
		{
			internal string NodeId;

			internal int LatencyMs;

			internal int JitterMs;

			internal int Failures;

			internal long MeasuredTicks;

			internal long FailedTicks;

			internal bool Fresh()
			{
				return MeasuredTicks > 0 && DateTime.UtcNow.Ticks - MeasuredTicks <= TimeSpan.FromMinutes(15.0).Ticks;
			}

			internal bool InCooldown()
			{
				return Failures >= 2 && DateTime.UtcNow.Ticks - FailedTicks <= TimeSpan.FromMinutes(10.0).Ticks;
			}

			internal int Score()
			{
				if (LatencyMs <= 0)
				{
					return 100000;
				}
				return LatencyMs + JitterMs * 2 / 3;
			}
		}

		private static readonly object gate = new object();

		private static Dictionary<string, Entry> entries;

		private static string PathFile()
		{
			return Path.Combine(NetNodePool.CacheDir(), "health.txt");
		}

		private static Dictionary<string, Entry> Map()
		{
			lock (gate)
			{
				if (entries != null)
				{
					return entries;
				}
				Dictionary<string, Entry> dictionary = new Dictionary<string, Entry>(StringComparer.Ordinal);
				try
				{
					if (File.Exists(PathFile()))
					{
						string[] array = File.ReadAllLines(PathFile(), Encoding.UTF8);
						foreach (string text in array)
						{
							Entry entry = Decode(text);
							if (entry != null && entry.NodeId != null)
							{
								dictionary[entry.NodeId] = entry;
							}
						}
					}
				}
				catch
				{
				}
				entries = dictionary;
				return entries;
			}
		}

		private static Entry Decode(string line)
		{
			string[] array = line.Split('|');
			if (array.Length < 6)
			{
				return null;
			}
			try
			{
				Entry entry = new Entry();
				entry.NodeId = array[0];
				entry.LatencyMs = int.Parse(array[1], CultureInfo.InvariantCulture);
				entry.JitterMs = int.Parse(array[2], CultureInfo.InvariantCulture);
				entry.Failures = int.Parse(array[3], CultureInfo.InvariantCulture);
				entry.MeasuredTicks = long.Parse(array[4], CultureInfo.InvariantCulture);
				entry.FailedTicks = long.Parse(array[5], CultureInfo.InvariantCulture);
				return entry;
			}
			catch
			{
				return null;
			}
		}

		private static string Encode(Entry entry)
		{
			return string.Concat(entry.NodeId, "|", entry.LatencyMs.ToString(CultureInfo.InvariantCulture), "|", entry.JitterMs.ToString(CultureInfo.InvariantCulture), "|", entry.Failures.ToString(CultureInfo.InvariantCulture), "|", entry.MeasuredTicks.ToString(CultureInfo.InvariantCulture), "|", entry.FailedTicks.ToString(CultureInfo.InvariantCulture));
		}

		internal static Entry Get(string nodeId)
		{
			Dictionary<string, Entry> dictionary = Map();
			lock (gate)
			{
				Entry value;
				if (dictionary.TryGetValue(nodeId ?? "", out value))
				{
					return value;
				}
			}
			return null;
		}

		internal static int Score(string nodeId)
		{
			Entry entry = Get(nodeId);
			if (entry == null)
			{
				return 100000;
			}
			return entry.Score();
		}

		internal static void Record(string nodeId, int latencyMs, int jitterMs, bool failed)
		{
			if (string.IsNullOrEmpty(nodeId))
			{
				return;
			}
			Dictionary<string, Entry> dictionary = Map();
			lock (gate)
			{
				Entry entry;
				if (!dictionary.TryGetValue(nodeId, out entry))
				{
					entry = new Entry();
					entry.NodeId = nodeId;
					dictionary[nodeId] = entry;
				}
				long utcNow = DateTime.UtcNow.Ticks;
				if (failed)
				{
					entry.Failures++;
					entry.FailedTicks = utcNow;
					entry.LatencyMs = 0;
					entry.JitterMs = 0;
					entry.MeasuredTicks = utcNow;
				}
				else if (latencyMs > 0)
				{
					entry.Failures = 0;
					entry.LatencyMs = latencyMs;
					entry.JitterMs = Math.Max(0, jitterMs);
					entry.MeasuredTicks = utcNow;
				}
				Save(dictionary);
			}
		}

		internal static void Clear()
		{
			lock (gate)
			{
				Dictionary<string, Entry> dictionary = Map();
				dictionary.Clear();
				Save(dictionary);
			}
		}

		private static void Save(Dictionary<string, Entry> dictionary)
		{
			try
			{
				List<string> list = new List<string>(dictionary.Count);
				foreach (KeyValuePair<string, Entry> item in dictionary)
				{
					// Drop stale rows so the file cannot grow forever.
					if (DateTime.UtcNow.Ticks - item.Value.MeasuredTicks > TimeSpan.FromDays(7.0).Ticks)
					{
						continue;
					}
					list.Add(Encode(item.Value));
				}
				AtomicFile.WriteLines(PathFile(), list.ToArray(), "netroute-health");
			}
			catch
			{
			}
		}
	}
}
