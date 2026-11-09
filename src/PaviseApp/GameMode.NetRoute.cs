using System;

namespace PaviseApp
{
	internal partial class GameMode
	{
		// The route is owned for as long as the game process lives. Switch-out is
		// intentionally not a stop condition: alt-tabbing must keep the
		// accelerator working, and only the session end or app close tears it down.
		private void StartSessionNetRoute(PolicySnapshot snapshot)
		{
			try
			{
				if (snapshot == null || snapshot.ValueOf(PolicyCatalog.KeyNetRoute) != "1")
				{
					return;
				}
				if (!Settings.Load("NetRouteEnabled", true))
				{
					return;
				}
				GameProfile gameProfile = null;
				lock (sync)
				{
					if (activeDetection != null)
					{
						gameProfile = activeDetection.Profile;
					}
				}
				if (gameProfile == null)
				{
					return;
				}
				string text = gameProfile.PreferredExecutablePath;
				if (string.IsNullOrEmpty(text))
				{
					return;
				}
				if (NetRouteSession.IsActive())
				{
					return;
				}
				NetRouteSession.Begin(gameProfile.Id, text);
			}
			catch (Exception error)
			{
				Logger.LogFailure("netroute begin", error);
			}
		}

		private bool EndSessionNetRoute(string reason)
		{
			try
			{
				bool flag = NetRouteSession.End(reason);
				if (!flag)
				{
					Logger.Warn("netroute: restore did not clear every record");
				}
				return flag;
			}
			catch (Exception error)
			{
				Logger.LogFailure("netroute end", error);
				return false;
			}
		}
	}
}
