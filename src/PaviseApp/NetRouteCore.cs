using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace PaviseApp
{
	// Owns the mihomo core process: locating or downloading it, writing the
	// config, waiting for the controller, talking to the controller and shutting
	// it down again. Every spawn is journaled so a crashed session can be healed.
	internal static class NetRouteCore
	{
		internal const string CorePathKey = "NetRouteCorePath";

		internal const string CoreUrlsKey = "NetRouteCoreUrls";

		internal const string ReceiptKey = "NetRouteCoreReceipt";

		internal const string ControllerPortKey = "NetRouteControllerPort";

		internal const string SecretKey = "NetRouteSecret";

		internal const string TestUrl = "http://www.gstatic.com/generate_204";

		internal const string WintunUrl = "https://www.wintun.net/builds/wintun-0.14.1.zip";

		private static readonly string[] KnownNames = new string[4] { "mihomo.exe", "clash-meta.exe", "verge-mihomo.exe", "mihomo-windows-amd64.exe" };

		private static readonly object gate = new object();

		private static Process running;

		private static int runningPort;

		private static string runningSecret;

		private static string status = "";

		internal static string CoreWorkDir()
		{
			string text = Path.Combine(NetNodePool.CacheDir(), "core");
			try
			{
				Directory.CreateDirectory(text);
			}
			catch
			{
			}
			return text;
		}

		internal static string ConfigPath(string name)
		{
			return Path.Combine(NetNodePool.CacheDir(), name + ".yaml");
		}

		internal static string Status()
		{
			lock (gate)
			{
				return status;
			}
		}

		private static void SetStatus(string text)
		{
			lock (gate)
			{
				status = text;
			}
		}

		internal static bool IsRunning()
		{
			lock (gate)
			{
				if (running == null)
				{
					return false;
				}
				try
				{
					if (!running.HasExited)
					{
						return true;
					}
				}
				catch
				{
				}
				return false;
			}
		}

		internal static string Locate()
		{
			try
			{
				string text = Settings.LoadStr(CorePathKey, "");
				if (text.Length > 0 && File.Exists(text))
				{
					return text;
				}
				string directoryName = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
				string[] array = KnownNames;
				foreach (string text2 in array)
				{
					string text3 = Path.Combine(directoryName, text2);
					if (File.Exists(text3))
					{
						return text3;
					}
					string text4 = Path.Combine(CoreWorkDir(), text2);
					if (File.Exists(text4))
					{
						return text4;
					}
				}
			}
			catch
			{
			}
			return null;
		}

		internal static string WintunState(string corePath)
		{
			try
			{
				string directoryName = Path.GetDirectoryName(corePath);
				if (directoryName == null)
				{
					return "missing";
				}
				string text = Path.Combine(directoryName, "wintun.dll");
				return File.Exists(text) ? "ok" : "missing";
			}
			catch
			{
				return "missing";
			}
		}

		// Downloads and unpacks the core when nothing usable is present. Returns
		// the executable path, or null with a human-readable reason.
		internal static string Install(out string reason)
		{
			reason = "";
			string text = Locate();
			if (text != null)
			{
				return text;
			}
			string text2 = Settings.LoadStr(CoreUrlsKey, "");
			string text3 = null;
			string lastError = "";
			if (text2.Length > 0)
			{
				string[] array = text2.Replace("\r", "").Split(new char[2] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries);
				foreach (string text4 in array)
				{
					string text5;
					if (DownloadAndUnpack(text4.Trim(), out text5, out lastError))
					{
						text3 = text5;
						break;
					}
				}
			}
			else
			{
				string text6;
				if (!ResolveLatestCoreUrl(out text6, out lastError))
				{
					reason = Lang.T("netroute.core.nourl") + " " + lastError;
					return null;
				}
				string text5;
				if (DownloadAndUnpack(text6, out text5, out lastError))
				{
					text3 = text5;
				}
				else
				{
					string text7 = "https://mirror.ghproxy.com/" + text6;
					if (DownloadAndUnpack(text7, out text5, out lastError))
					{
						text3 = text5;
					}
				}
			}
			if (text3 == null)
			{
				reason = Lang.T("netroute.core.dlfail") + " " + lastError;
				return null;
			}
			Settings.SaveStr(CorePathKey, text3);
			Logger.Log(Lang.F("netroute.core.installed", text3));
			string text8;
			if (!EnsureWintun(text3, out text8))
			{
				Logger.Warn("netroute wintun: " + text8);
			}
			return text3;
		}

		private static bool ResolveLatestCoreUrl(out string url, out string error)
		{
			url = null;
			error = "";
			EnableModernTls();
			try
			{
				HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/MetaCubeX/mihomo/releases/latest");
				httpWebRequest.Timeout = 8000;
				httpWebRequest.ReadWriteTimeout = 8000;
				httpWebRequest.UserAgent = "Pavise/" + Logger.Version;
				httpWebRequest.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
				httpWebRequest.Proxy = null;
				string text;
				using (HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse())
				{
					using (StreamReader streamReader = new StreamReader(httpWebResponse.GetResponseStream(), Encoding.UTF8))
					{
						text = streamReader.ReadToEnd();
					}
				}
				string[] array = text.Split(new string[2] { "\"browser_download_url\":", "\"browser_download_url\": " }, StringSplitOptions.None);
				for (int i = 1; i < array.Length; i++)
				{
					int num = array[i].IndexOf('"');
					if (num < 0)
					{
						continue;
					}
					int num2 = array[i].IndexOf('"', num + 1);
					if (num2 < 0)
					{
						continue;
					}
					string text2 = array[i].Substring(num + 1, num2 - num - 1);
					if (text2.IndexOf("windows-amd64", StringComparison.OrdinalIgnoreCase) >= 0 && text2.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
					{
						url = text2;
						return true;
					}
				}
				error = "windows-amd64 asset not found";
				return false;
			}
			catch (Exception ex)
			{
				error = ex.Message;
				return false;
			}
		}

		private static bool DownloadAndUnpack(string url, out string exePath, out string error)
		{
			exePath = null;
			error = "";
			EnableModernTls();
			string text = Path.Combine(NetNodePool.CacheDir(), "core.zip");
			try
			{
				HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(url);
				httpWebRequest.Timeout = 30000;
				httpWebRequest.ReadWriteTimeout = 60000;
				httpWebRequest.UserAgent = "Pavise/" + Logger.Version;
				httpWebRequest.Proxy = null;
				using (HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse())
				{
					if (httpWebResponse.StatusCode != HttpStatusCode.OK)
					{
						error = "status " + (int)httpWebResponse.StatusCode;
						return false;
					}
					using (Stream responseStream = httpWebResponse.GetResponseStream())
					{
						using (FileStream fileStream = new FileStream(text, FileMode.Create, FileAccess.Write, FileShare.None))
						{
							responseStream.CopyTo(fileStream);
						}
					}
				}
				string text2 = Path.Combine(CoreWorkDir(), "unpack");
				try
				{
					if (Directory.Exists(text2))
					{
						Directory.Delete(text2, true);
					}
					Directory.CreateDirectory(text2);
				}
				catch
				{
				}
				string text3 = string.Concat("$ErrorActionPreference = 'Stop'\n", "Expand-Archive -LiteralPath '", text.Replace("'", "''"), "' -DestinationPath '", text2.Replace("'", "''"), "' -Force\n", "Write-Output 'UNPACKOK'");
				string text4;
				if (!PsRunner.Run(text3, "netroute-unpack", 60000, out text4) || text4.IndexOf("UNPACKOK", StringComparison.Ordinal) < 0)
				{
					error = "unpack failed";
					return false;
				}
				string[] array = KnownNames;
				foreach (string text5 in array)
				{
					string text6 = Path.Combine(text2, text5);
					if (File.Exists(text6))
					{
						exePath = text6;
						break;
					}
				}
				if (exePath == null && Directory.Exists(text2))
				{
					string[] files = Directory.GetFiles(text2, "*.exe", SearchOption.AllDirectories);
					string[] array2 = files;
					foreach (string text7 in array2)
					{
						string fileName = Path.GetFileName(text7);
						if (fileName.IndexOf("mihomo", StringComparison.OrdinalIgnoreCase) >= 0 || fileName.IndexOf("clash", StringComparison.OrdinalIgnoreCase) >= 0)
						{
							exePath = text7;
							break;
						}
					}
				}
				if (exePath == null)
				{
					error = "exe not found after unpack";
					return false;
				}
				return true;
			}
			catch (Exception ex)
			{
				error = ex.GetType().Name + ": " + ex.Message;
				return false;
			}
		}

		// mihomo releases ship without wintun, which TUN mode needs. Fetch the
		// official amd64 build once and drop it next to the core.
		internal static bool EnsureWintun(string corePath, out string error)
		{
			error = "";
			if (WintunState(corePath) == "ok")
			{
				return true;
			}
			string text = Path.GetDirectoryName(corePath);
			if (string.IsNullOrEmpty(text))
			{
				error = "core directory unavailable";
				return false;
			}
			EnableModernTls();
			string text2 = Path.Combine(text, "wintun.zip");
			try
			{
				HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(WintunUrl);
				httpWebRequest.Timeout = 8000;
				httpWebRequest.ReadWriteTimeout = 20000;
				httpWebRequest.UserAgent = "Pavise/" + Logger.Version;
				httpWebRequest.Proxy = null;
				using (HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse())
				{
					using (Stream responseStream = httpWebResponse.GetResponseStream())
					{
						using (FileStream fileStream = new FileStream(text2, FileMode.Create, FileAccess.Write, FileShare.None))
						{
							responseStream.CopyTo(fileStream);
						}
					}
				}
				string text3 = Path.Combine(text, "wintun-unpack");
				try
				{
					if (Directory.Exists(text3))
					{
						Directory.Delete(text3, true);
					}
					Directory.CreateDirectory(text3);
				}
				catch
				{
				}
				string text4 = string.Concat("$ErrorActionPreference = 'Stop'\n", "Expand-Archive -LiteralPath '", text2.Replace("'", "''"), "' -DestinationPath '", text3.Replace("'", "''"), "' -Force\n", "Write-Output 'UNPACKOK'");
				string text5;
				if (!PsRunner.Run(text4, "netroute-wintun", 45000, out text5) || text5.IndexOf("UNPACKOK", StringComparison.Ordinal) < 0)
				{
					error = "wintun unpack failed";
					return false;
				}
				string[] array = Directory.GetFiles(text3, "wintun.dll", SearchOption.AllDirectories);
				string text6 = null;
				foreach (string text7 in array)
				{
					if (text7.IndexOf("amd64", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						text6 = text7;
						break;
					}
				}
				if (text6 == null && array.Length > 0)
				{
					text6 = array[0];
				}
				if (text6 == null)
				{
					error = "wintun.dll not found";
					return false;
				}
				File.Copy(text6, Path.Combine(text, "wintun.dll"), true);
				return true;
			}
			catch (Exception ex)
			{
				error = ex.GetType().Name + ": " + ex.Message;
				return false;
			}
		}

		internal static string Secret(bool createIfMissing)
		{
			string text = Settings.LoadStr(SecretKey, "");
			if (text.Length >= 16)
			{
				return text;
			}
			if (!createIfMissing)
			{
				return text;
			}
			byte[] array = new byte[16];
			Random random = new Random();
			for (int i = 0; i < array.Length; i++)
			{
				array[i] = (byte)random.Next(32, 127);
			}
			string text2 = Convert.ToBase64String(array).Replace("/", "").Replace("+", "").Replace("=", "");
			Settings.SaveStr(SecretKey, text2);
			return text2;
		}

		private static int PickPort(int low, int high)
		{
			Random random = new Random();
			int num = low + random.Next(0, high - low);
			return num;
		}

		// Starts the core with the given config file, waits for the controller.
		internal static bool Start(string corePath, string configPath, int controllerPort, string secret, out string error)
		{
			error = "";
			lock (gate)
			{
				if (running != null)
				{
					if (!running.HasExited)
					{
						error = Lang.T("netroute.core.busy");
						return false;
					}
					running = null;
				}
			}
			try
			{
				ProcessStartInfo processStartInfo = new ProcessStartInfo();
				processStartInfo.FileName = corePath;
				processStartInfo.Arguments = "-d " + QuoteArg(CoreWorkDir()) + " -f " + QuoteArg(configPath);
				processStartInfo.UseShellExecute = false;
				processStartInfo.CreateNoWindow = true;
				processStartInfo.WorkingDirectory = Path.GetDirectoryName(corePath);
				processStartInfo.RedirectStandardOutput = false;
				processStartInfo.RedirectStandardError = false;
				Process process = new Process();
				process.StartInfo = processStartInfo;
				if (!process.Start())
				{
					error = "process start failed";
					return false;
				}
				lock (gate)
				{
					running = process;
					runningPort = controllerPort;
					runningSecret = secret;
				}
				int num = controllerPort;
				string text = secret;
				string text2;
				WaitReady(process, num, text, 8000, out text2);
				if (text2 != null)
				{
					error = text2;
					Stop(Lang.T("netroute.core.startfail"));
					return false;
				}
				RecordReceipt(process, controllerPort);
				return true;
			}
			catch (Exception ex)
			{
				error = ex.GetType().Name + ": " + ex.Message;
				Stop(Lang.T("netroute.core.startfail"));
				return false;
			}
		}

		private static string QuoteArg(string path)
		{
			return "\"" + (path ?? "").Replace("\"", "") + "\"";
		}

		private static void WaitReady(Process process, int port, string secret, int timeoutMs, out string error)
		{
			error = null;
			long tickCount = Environment.TickCount;
			while (Environment.TickCount - tickCount < timeoutMs)
			{
				bool flag;
				try
				{
					flag = process.HasExited;
				}
				catch
				{
					flag = true;
				}
				if (flag)
				{
					error = Lang.T("netroute.core.exited");
					return;
				}
				string text;
				if (ControllerGet(port, secret, "/version", 1200, out text) && text != null && text.Length > 0)
				{
					error = null;
					return;
				}
				Thread.Sleep(250);
			}
			error = Lang.T("netroute.core.notready");
		}

		private static void RecordReceipt(Process process, int port)
		{
			try
			{
				string text = process.Id.ToString(CultureInfo.InvariantCulture) + "|" + process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture) + "|" + port.ToString(CultureInfo.InvariantCulture);
				Settings.SaveStr(ReceiptKey, text);
				Settings.SaveStr(ControllerPortKey, port.ToString(CultureInfo.InvariantCulture));
			}
			catch
			{
			}
		}

		internal static bool ControllerGet(int port, string secret, string path, int timeoutMs, out string body)
		{
			body = null;
			try
			{
				HttpWebRequest httpWebRequest = NewControllerRequest(port, secret, path, "GET", timeoutMs);
				using (HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse())
				{
					using (StreamReader streamReader = new StreamReader(httpWebResponse.GetResponseStream(), Encoding.UTF8))
					{
						body = streamReader.ReadToEnd();
					}
				}
				return true;
			}
			catch (WebException ex)
			{
				HttpWebResponse httpWebResponse2 = ex.Response as HttpWebResponse;
				if (httpWebResponse2 != null)
				{
					using (StreamReader streamReader2 = new StreamReader(httpWebResponse2.GetResponseStream(), Encoding.UTF8))
					{
						body = streamReader2.ReadToEnd();
					}
					httpWebResponse2.Close();
				}
				return false;
			}
			catch
			{
				return false;
			}
		}

		private static HttpWebRequest NewControllerRequest(int port, string secret, string path, string method, int timeoutMs)
		{
			HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + path);
			httpWebRequest.Method = method;
			httpWebRequest.Timeout = Math.Max(600, timeoutMs);
			httpWebRequest.ReadWriteTimeout = Math.Max(600, timeoutMs);
			httpWebRequest.ContentType = "application/json";
			httpWebRequest.Proxy = null;
			httpWebRequest.KeepAlive = false;
			if (!string.IsNullOrEmpty(secret))
			{
				httpWebRequest.Headers["Authorization"] = "Bearer " + secret;
			}
			return httpWebRequest;
		}

		internal static bool ControllerPut(int port, string secret, string path, string json, int timeoutMs)
		{
			try
			{
				HttpWebRequest httpWebRequest = NewControllerRequest(port, secret, path, "PUT", timeoutMs);
				byte[] bytes = Encoding.UTF8.GetBytes(json ?? "{}");
				httpWebRequest.ContentLength = bytes.Length;
				using (Stream requestStream = httpWebRequest.GetRequestStream())
				{
					requestStream.Write(bytes, 0, bytes.Length);
				}
				using (HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse())
				{
					return httpWebResponse.StatusCode == HttpStatusCode.NoContent || httpWebResponse.StatusCode == HttpStatusCode.OK;
				}
			}
			catch (WebException ex)
			{
				HttpWebResponse httpWebResponse2 = ex.Response as HttpWebResponse;
				if (httpWebResponse2 != null)
				{
					int statusCode = (int)httpWebResponse2.StatusCode;
					httpWebResponse2.Close();
					return statusCode >= 200 && statusCode < 300;
				}
				return false;
			}
			catch
			{
				return false;
			}
		}

		internal static bool SelectNode(string tag)
		{
			lock (gate)
			{
				if (running == null || runningPort <= 0)
				{
					return false;
				}
			}
			string text = "{\"name\":\"" + (tag ?? "").Replace("\"", "") + "\"}";
			return ControllerPut(runningPort, runningSecret, "/proxies/" + Uri.EscapeDataString(NetRouteConfig.GameGroup), text, 3000);
		}

		internal static int Delay(string tag, int timeoutMs)
		{
			lock (gate)
			{
				if (running == null || runningPort <= 0)
				{
					return -1;
				}
			}
			string text = "/proxies/" + Uri.EscapeDataString(tag) + "/delay?timeout=" + timeoutMs.ToString(CultureInfo.InvariantCulture) + "&url=" + Uri.EscapeDataString(NetRouteConfig.TestUrl);
			string body;
			if (!ControllerGet(runningPort, runningSecret, text, timeoutMs + 1500, out body) || body == null)
			{
				return -1;
			}
			int num = body.IndexOf("\"delay\"", StringComparison.Ordinal);
			if (num < 0)
			{
				return -1;
			}
			int num2 = body.IndexOf(':', num);
			if (num2 < 0)
			{
				return -1;
			}
			int num3 = num2 + 1;
			while (num3 < body.Length && (char.IsWhiteSpace(body[num3]) || body[num3] == '"'))
			{
				num3++;
			}
			int num4 = num3;
			while (num4 < body.Length && char.IsDigit(body[num4]))
			{
				num4++;
			}
			int result;
			if (int.TryParse(body.Substring(num3, num4 - num3), NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
			{
				return result;
			}
			return -1;
		}

		internal static int ControllerPort()
		{
			lock (gate)
			{
				if (runningPort > 0)
				{
					return runningPort;
				}
			}
			return PickPort(39100, 49900);
		}

		internal static int PickMixedPort()
		{
			return PickPort(39100, 49900);
		}

		// Stops the core we started. Safe to call when nothing is running.
		internal static bool Stop(string reason)
		{
			Process process = null;
			lock (gate)
			{
				process = running;
				running = null;
				runningPort = 0;
				runningSecret = null;
			}
			if (process != null)
			{
				try
				{
					if (!process.HasExited)
					{
						process.Kill();
					}
					process.WaitForExit(4000);
				}
				catch
				{
				}
				try
				{
					process.Dispose();
				}
				catch
				{
				}
			}
			Settings.SaveStr(ReceiptKey, "");
			try
			{
				string text = ConfigPath("route");
				if (File.Exists(text))
				{
					File.Delete(text);
				}
			}
			catch
			{
			}
			if (!string.IsNullOrEmpty(reason))
			{
				Logger.Log(Lang.F("netroute.core.stopped", reason));
			}
			return true;
		}

		internal static bool HasResidue()
		{
			return Settings.LoadStr(ReceiptKey, "").Length > 0;
		}

		// Kills a core left behind by an earlier Pavise process. Only a process
		// whose start time matches the journal is touched.
		internal static bool HealFromCrash()
		{
			string text = Settings.LoadStr(ReceiptKey, "");
			bool flag = true;
			if (text.Length > 0)
			{
				string[] array = text.Split('|');
				if (array.Length >= 2)
				{
					try
					{
						int num = int.Parse(array[0], CultureInfo.InvariantCulture);
						long num2 = long.Parse(array[1], CultureInfo.InvariantCulture);
						using (Process process = Process.GetProcessById(num))
						{
							if (process.StartTime.ToUniversalTime().Ticks == num2)
							{
								process.Kill();
								process.WaitForExit(3000);
								Logger.Info("netroute: killed leftover core pid=" + num);
							}
						}
					}
					catch
					{
					}
				}
				Settings.SaveStr(ReceiptKey, "");
			}
			try
			{
				string text2 = ConfigPath("route");
				if (File.Exists(text2))
				{
					File.Delete(text2);
				}
			}
			catch
			{
				flag = false;
			}
			return flag;
		}

		internal static bool RunningProcessForDiagnostics(out int pid)
		{
			pid = 0;
			lock (gate)
			{
				if (running == null)
				{
					return false;
				}
				try
				{
					pid = running.Id;
					return true;
				}
				catch
				{
					return false;
				}
			}
		}
	}
}
