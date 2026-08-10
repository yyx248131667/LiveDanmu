using System;

namespace LiveDanmuDesktop.Models;

public class MuyuHitEventArgs : EventArgs
{
	public int Hits { get; set; }

	public string Text { get; set; } = "";

	public string User { get; set; } = "";

	public string LogContent { get; set; } = "";

	public string Platform { get; set; } = "";

	public string Method { get; set; } = "";

	public int TotalCount { get; set; }

	public int LikeCount { get; set; }

	public int GiftCount { get; set; }

	public bool PlaySound { get; set; }

	public int Volume { get; set; }

	public int AudioSpeed { get; set; }

	public int LikeRate { get; set; }

	public int GiftRate { get; set; }
}
