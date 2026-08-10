using System;

namespace LiveDanmuDesktop;

public class CookieErrorEventArgs : EventArgs
{
	public string ErrorMessage { get; set; } = string.Empty;

	public int RetryCount { get; set; }
}
