using System;

namespace LiveDanmuDesktop.Models;

public class MuyuConfigEventArgs : EventArgs
{
	public string Platform { get; set; } = "";

	public PlatformMuyuConfig Config { get; set; } = new PlatformMuyuConfig();
}
