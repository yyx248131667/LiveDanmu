using System;

namespace LiveDanmuDesktop.Models;

public class GiftCollectedEventArgs : EventArgs
{
	public string Platform { get; set; } = "";

	public string GiftId { get; set; } = "";

	public string GiftName { get; set; } = "";

	public int TotalGifts { get; set; }
}
