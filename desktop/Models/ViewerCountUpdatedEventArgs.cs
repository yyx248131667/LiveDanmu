using System;

namespace LiveDanmuDesktop.Models;

public class ViewerCountUpdatedEventArgs : EventArgs
{
	public string Platform { get; set; } = "";

	public int Count { get; set; }
}
