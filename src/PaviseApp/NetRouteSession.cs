using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace PaviseApp
{
	// Session-scoped network route. Work runs off the game loop on its own
	// thread: probing up to a few dozen nodes must never block the scheduler.
	// The route deliberately ignores foreground changes; it lives as long as the
	// game process does and is only torn down when the app closes the session.
	internal static class NetRouteSession
	{
		internal const string PolicyKey = "GmNetRouteV1";

		internal const int ProbeSampleTimeoutMs = 2200;

		internal const int ProbeParallel = 8;

		internal const int ProbeNodeLimit = 40;

		private static readonly object gate = new object();

		private static int state;

		private static int generation;

		private static volatile bool cancelling;

		private static string profileId = "";

		private static string gameExecutable = "";

		private static string selectedLabel = "";

		private static string selectedId = "";

		private static int selectedLatency;

		private static string message = "";

		private static long sampleTicks;

		private static Thread worker;

		private static Thread monitor;

		private static volatile bool monitorStop;

		internal const int StateIdle = 0;

		internal const int StatePreparing = 1;

		internal const int StateActive = 2;

		internal const int StateFailed = 3;

		internal static int State
		{
			get
			{
				return Volatile.Read(ref state);
			}
		}

		internal static bool IsActive()
		{
			return State == StateActive && NetRouteCore.IsRunning();
		}

		internal static bool HasResidue()
		{
			return NetRouteCore.HasResidue() || State == StateActive || State == StatePreparing;
		}

		internal static string StatusText()
		{
			lock (gate)
			{
				switch (state)
				{
				case StatePreparing:
					return Lang.T("netroute.state.preparing");
				case StateActive:
				{
					string text = Lang.F("netroute.state.active", selectedLabel);
					if (selectedLatency > 0)
					{
						text = text + " · " + selectedLatency.ToString(CultureInfo.InvariantCulture) + " ms";
					}
					if ((message ?? "").Length > 0)
					{
						text = text + " · " + message;
					}
					return text;
				}
				case StateFailed:
					return Lang.T("netroute.state.failed") + ((message ?? "").Length > 0 ? (" · " + message) : "");
				default:
					return Lang.T("netroute.state.idle");
				}
			}
		}

		internal static string SelectedLabel()
		{
			lock (gate)
			{
				return selectedLabel ?? "";
			}
		}

		// Called from BeginSessionPolicy. Non-blocking.
		internal static bool Begin(string profile, string gameExe)
		{
			// A different game taking over the session ends the previous route.
			if (IsActive())
			{
				lock (gate)
				{
					if (!string.Equals(profileId, profile ?? "", StringComparison.OrdinalIgnoreCase) || !string.Equals(gameExecutable, gameExe ?? "", StringComparison.OrdinalIgnoreCase))
					{
						End("Switch");
					}
				}
			}
			int num;
			lock (gate)
			{
				num = ++generation;
				cancelling = false;
				state = StatePreparing;
				message = "";
				selectedLabel = "";
				selectedId = "";
				selectedLatency = 0;
				profileId = profile ?? "";
				gameExecutable = gameExe ?? "";
			}
			Logger.Log(Lang.F("netroute.log.begin", profileId, gameExecutable));
			StartMonitor();
			Thread thread = new Thread((ThreadStart)delegate { Run(num); });
			thread.IsBackground = true;
			thread.Name = "PaviseNetRoute";
			lock (gate)
			{
				worker = thread;
			}
			try
			{
				thread.Start();
				return true;
			}
			catch (Exception ex)
			{
				lock (gate)
				{
					if (generation == num)
					{
						state = StateFailed;
						message = ex.GetType().Name;
					}
				}
				return false;
			}
		}

		private static bool Canceled(int token)
		{
			lock (gate)
			{
				return cancelling || token != generation;
			}
		}

		private static void Run(int token)
		{
			try
			{
				List<string> urls = null;
				List<NetNode> list = NetNodePool.Load(false);
				if (Canceled(token))
				{
					return;
				}
				if (list.Count == 0 && NetNodePool.DueForRefresh() && NetNodePool.SubscriptionUrls(out urls) > 0)
				{
					string error;
					NetNodePool.RefreshSubscriptions(8000, out error);
					list = NetNodePool.Load(true);
					if (Canceled(token))
					{
						return;
					}
				}
				if (list.Count == 0)
				{
					Fail(token, Lang.T("netroute.err.nonodes"), Lang.T("netroute.err.nonodes.hint"));
					return;
				}
				string corePath = NetRouteCore.Locate();
				if (corePath == null)
				{
					if (!Settings.Load("NetRouteAutoInstallCore", true))
					{
						Fail(token, Lang.T("netroute.err.nocore"), Lang.T("netroute.err.nocore.hint"));
						return;
					}
					string text2;
					corePath = NetRouteCore.Install(out text2);
					if (Canceled(token))
					{
						return;
					}
					if (corePath == null)
					{
						Fail(token, Lang.T("netroute.err.nocore"), text2);
						return;
					}
				}
				if (NetRouteCore.WintunState(corePath) != "ok")
				{
					Fail(token, Lang.T("netroute.err.wintun"), Lang.T("netroute.err.wintun.hint"));
					return;
				}
				int num = NetRouteCore.PickMixedPort();
				int num2 = num + 1;
				string text3 = NetNodePool.CacheDir();
				string text4 = Path.Combine(text3, "probe.yaml");
				string text6 = NetRouteCore.Secret(true);
				string content = NetRouteConfig.Build(false, num, num2, text6, list, null);
				string text7;
				bool flag = NetNodeConfigWrite(text4, content, out text7);
				if (Canceled(token))
				{
					return;
				}
				if (!flag)
				{
					Fail(token, Lang.T("netroute.err.prepare"), text7);
					return;
				}
				string text8;
				if (!NetRouteCore.Start(corePath, text4, num2, text6, out text8))
				{
					if (Canceled(token))
					{
						NetRouteCore.Stop("");
						return;
					}
					Fail(token, Lang.T("netroute.err.prepare"), text8);
					return;
				}
				ProbeAll(list, token);
				NetRouteCore.Stop(Lang.T("netroute.log.probe.done"));
				if (Canceled(token))
				{
					return;
				}
				NetNodeSelector.Selection selection = NetNodeSelector.Choose(profileId, list);
				if (selection == null || selection.Node == null)
				{
					Fail(token, Lang.T("netroute.err.noselect"), selection == null ? "" : selection.Reason);
					return;
				}
				NetNode node = selection.Node;
				string text9 = NetRouteConfig.Build(true, NetRouteCore.PickMixedPort(), num2, text6, list, gameExecutable);
				string text10 = NetRouteCore.ConfigPath("route");
				string text11;
				if (!NetNodeConfigWrite(text10, text9, out text11))
				{
					Fail(token, Lang.T("netroute.err.prepare"), text11);
					return;
				}
				if (Canceled(token))
				{
					TryDelete(text10);
					return;
				}
				string text12;
				if (!NetRouteCore.Start(corePath, text10, num2, text6, out text12))
				{
					Fail(token, Lang.T("netroute.err.prepare"), text12);
					return;
				}
				if (Canceled(token))
				{
					NetRouteCore.Stop(Lang.T("netroute.log.aborted"));
					return;
				}
				if (!NetRouteCore.SelectNode(node.Tag))
				{
					NetRouteCore.Stop(Lang.T("netroute.log.selectfail"));
					Fail(token, Lang.T("netroute.err.select"), node.DisplayLabel());
					return;
				}
				int num3 = NetRouteCore.Delay(node.Tag, 3000);
				if (num3 <= 0)
				{
					// The core is up but this node does not answer; fall back to any
					// node that does before giving up on the session.
					NodeFallback(token, list, node, num3);
					return;
				}
				Publish(token, node, num3, selection.Reason);
			}
			catch (Exception ex)
			{
				Logger.LogFailure("netroute session", ex);
				Fail(token, Lang.T("netroute.err.exception"), ex.GetType().Name + ": " + ex.Message);
			}
		}

		private static void NodeFallback(int token, List<NetNode> list, NetNode chosen, int delay)
		{
			NetNodeHealth.Record(chosen.Id, 0, 0, true);
			foreach (NetNode node in list)
			{
				if (node.Id == chosen.Id || Canceled(token))
				{
					continue;
				}
				int num = NetRouteCore.Delay(node.Tag, 2500);
				if (num > 0)
				{
					NetNodeHealth.Record(node.Id, num, 0, false);
					if (NetRouteCore.SelectNode(node.Tag))
					{
						NetNodeSelector.Remember(profileId, node.Id);
						Publish(token, node, num, Lang.T("netroute.select.fallback"));
						return;
					}
				}
				NetNodeHealth.Record(node.Id, 0, 0, true);
			}
			NetRouteCore.Stop(Lang.T("netroute.log.nodefail"));
			Fail(token, Lang.T("netroute.err.nodelatency"), chosen.DisplayLabel() + " " + delay.ToString(CultureInfo.InvariantCulture));
		}

		private static bool NetNodeConfigWrite(string path, string content, out string error)
		{
			return NetRouteConfig.WriteConfig(path, content, out error);
		}

		// Probes every enabled node through the core, so latency, handshake and
		// protocol support are all exercised exactly as the game will see them.
		 private static void ProbeAll(List<NetNode> nodes, int token)
		{
			List<NetNode> list = new List<NetNode>();
			foreach (NetNode node in nodes)
			{
				if (node.Usable)
				{
					NetNodeHealth.Entry entry = NetNodeHealth.Get(node.Id);
					if (entry == null || !entry.InCooldown())
					{
						list.Add(node);
					}
				}
			}
			if (list.Count > ProbeNodeLimit)
			{
				list.Sort(delegate(NetNode a, NetNode b)
				{
					return NetNodeHealth.Score(a.Id).CompareTo(NetNodeHealth.Score(b.Id));
				});
				list.RemoveRange(ProbeNodeLimit, list.Count - ProbeNodeLimit);
			}
			int num = 0;
			object obj = new object();
			int num2 = 0;
			while (num2 < list.Count)
			{
				if (Canceled(token))
				{
					return;
				}
				int num3 = Math.Min(ProbeParallel, list.Count - num2);
				ManualResetEvent[] array = new ManualResetEvent[num3];
				for (int i = 0; i < num3; i++)
				{
					NetNode node = list[num2 + i];
					array[i] = new ManualResetEvent(false);
					ManualResetEvent complete = array[i];
					ThreadPool.QueueUserWorkItem(delegate
					{
						try
						{
							int num4 = NetRouteCore.Delay(node.Tag, ProbeSampleTimeoutMs);
							lock (obj)
							{
								num++;
								NetNodeHealth.Record(node.Id, num4, 0, num4 <= 0);
							}
							Logger.Info("netroute probe " + node.Tag + " => " + num4.ToString(CultureInfo.InvariantCulture));
						}
						catch
						{
						}
						finally
						{
							complete.Set();
						}
					});
				}
				WaitHandle.WaitAll(array, 3500);
				for (int j = 0; j < array.Length; j++)
				{
					array[j].Close();
				}
				num2 += num3;
			}
			Logger.Info("netroute probe finished, measured=" + num.ToString(CultureInfo.InvariantCulture) + " of " + list.Count.ToString(CultureInfo.InvariantCulture));
		}

		private static void Publish(int token, NetNode node, int latency, string reason)
		{
			lock (gate)
			{
				if (token != generation)
				{
					return;
				}
				state = StateActive;
				selectedLabel = node.DisplayLabel();
				selectedId = node.Id;
				selectedLatency = latency;
				message = reason ?? "";
				sampleTicks = DateTime.UtcNow.Ticks;
			}
			NetNodeSelector.Remember(profileId, node.Id);
			Logger.Log(Lang.F("netroute.log.active", DisplayLabelSafe(node), latency.ToString(CultureInfo.InvariantCulture)));
		}

		private static string DisplayLabelSafe(NetNode node)
		{
			return node == null ? "" : node.DisplayLabel();
		}

		private static void Fail(int token, string summary, string detail)
		{
			lock (gate)
			{
				if (token != generation)
				{
					return;
				}
				state = StateFailed;
				message = string.IsNullOrEmpty(detail) ? (summary ?? "") : (summary + ": " + detail);
			}
			Logger.Warn("netroute failed: " + message);
		}

		private static void TryDelete(string path)
		{
			try
			{
				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
			catch
			{
			}
		}

		internal static bool GameProcessAlive()
		{
			string text = NetRouteConfig.SafeExeName(gameExecutable);
			if (string.IsNullOrEmpty(text) || text.Length <= 4)
			{
				return true;
			}
			try
			{
				Process[] processesByName = Process.GetProcessesByName(text.Substring(0, text.Length - 4));
				bool result = processesByName.Length > 0;
				foreach (Process process in processesByName)
				{
					try
					{
						process.Dispose();
					}
					catch
					{
					}
				}
				return result;
			}
			catch
			{
				return true;
			}
		}

		private static void StartMonitor()
		{
			if (monitor != null && monitor.IsAlive)
			{
				return;
			}
			monitorStop = false;
			Thread thread = new Thread((ThreadStart)MonitorLoop);
			thread.IsBackground = true;
			thread.Name = "PaviseNetRouteMonitor";
			monitor = thread;
			try
			{
				thread.Start();
			}
			catch
			{
			}
		}

		// Releases the route when the game process disappears, even if the
		// session tracker already moved on to another state.
		private static void MonitorLoop()
		{
			while (!monitorStop)
			{
				try
				{
					Thread.Sleep(2000);
					if (monitorStop)
					{
						break;
					}
					if (State != StateActive && State != StatePreparing)
					{
						continue;
					}
					if (GameProcessAlive())
					{
						continue;
					}
					Logger.Log("netroute monitor: game process gone");
					End("ProcessExit");
				}
				catch (Exception error)
				{
					Logger.LogFailure("netroute monitor", error);
				}
			}
		}

		// Session end / app close: stop the core and drop the receipts.
		// Alt-tab, minimise or losing foreground never reaches this method; a
		// session that ends while the game still runs keeps the route until the
		// process is gone, so an early detector decision cannot cut the game off.
		internal static bool End(string reason)
		{
			bool flag;
			if (!string.Equals(reason, "Stop", StringComparison.OrdinalIgnoreCase) && !string.Equals(reason, "Switch", StringComparison.OrdinalIgnoreCase) && !string.Equals(reason, "ProcessExit", StringComparison.OrdinalIgnoreCase) && GameProcessAlive())
			{
				Logger.Log("netroute keep: game process still alive (" + (reason ?? "") + ")");
				return true;
			}
			lock (gate)
			{
				generation++;
				cancelling = true;
				flag = state != StateIdle || NetRouteCore.HasResidue();
				state = StateIdle;
				selectedLabel = "";
				selectedId = "";
				selectedLatency = 0;
				message = "";
				profileId = "";
				gameExecutable = "";
			}
			NetRouteCore.Stop(reason ?? "");
			TryDelete(NetNodePool.CacheDir() + "\\probe.yaml");
			monitorStop = true;
			return !NetRouteCore.HasResidue() || !flag;
		}

		internal static bool HealFromCrash()
		{
			bool flag = NetRouteCore.HealFromCrash();
			lock (gate)
			{
				state = StateIdle;
				message = "";
				selectedLabel = "";
				selectedId = "";
				selectedLatency = 0;
			}
			TryDelete(NetNodePool.CacheDir() + "\\probe.yaml");
			TryDelete(NetRouteCore.ConfigPath("route"));
			return flag;
		}

		// Manual probe from the diagnostics card. Refuses while a session owns
		// the core so a game in progress never loses its route.
		internal static bool ProbeNow()
		{
			lock (gate)
			{
				if (state == StatePreparing || state == StateActive)
				{
					message = Lang.T("netroute.err.busy");
					return false;
				}
			}
			List<NetNode> list = NetNodePool.Load(true);
			if (list.Count == 0)
			{
				lock (gate)
				{
					state = StateFailed;
					message = Lang.T("netroute.err.nonodes");
				}
				return false;
			}
			string corePath = NetRouteCore.Locate();
			if (corePath == null)
			{
				string text;
				corePath = NetRouteCore.Install(out text);
				if (corePath == null)
				{
					lock (gate)
					{
						state = StateFailed;
						message = text;
					}
					return false;
				}
			}
			lock (gate)
			{
				state = StatePreparing;
				message = "";
			}
			int num = NetRouteCore.PickMixedPort();
			int num2 = num + 1;
			string text2 = NetRouteCore.Secret(true);
			string text3 = NetRouteCore.ConfigPath("probe");
			string text4;
			string text5 = NetRouteConfig.Build(false, num, num2, text2, list, null);
			if (!NetNodeConfigWrite(text3, text5, out text4))
			{
				lock (gate)
				{
					state = StateFailed;
					message = text4;
				}
				return false;
			}
			string text6;
			if (!NetRouteCore.Start(corePath, text3, num2, text2, out text6))
			{
				lock (gate)
				{
					state = StateFailed;
					message = text6;
				}
				return false;
			}
			ProbeAll(list, generation);
			NetRouteCore.Stop(Lang.T("netroute.log.probe.done"));
			TryDelete(text3);
			lock (gate)
			{
				state = StateIdle;
				message = Lang.F("netroute.probe.manual", list.Count);
			}
			return true;
		}
	}
}
