using System;

namespace LiveDanmuDesktop;

public class CookieFetchedEventArgs : EventArgs
{
	public string CookieString { get; set; } = string.Empty;

	public int CookieCount { get; set; }

	public string SavedPath { get; set; } = string.Empty;
}
