using System;

namespace LiveDanmuDesktop.Models;

public class DanmakuReceivedEventArgs : EventArgs
{
	public string MsgType { get; set; } = "";

	public string Platform { get; set; } = "";

	public string User { get; set; } = "";

	public string Content { get; set; } = "";
}
