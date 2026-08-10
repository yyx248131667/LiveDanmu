using System;

namespace LiveDanmuDesktop.Models.Taoyuan;

public class Player
{
	public string Name { get; set; } = "";

	public int Level { get; set; } = 1;

	public int Exp { get; set; } = 0;

	public int Gold { get; set; } = 0;

	public string TeamId { get; set; } = "";

	public string State { get; set; } = "Idle";

	public int X { get; set; }

	public int Y { get; set; }

	public DateTime LastActive { get; set; } = DateTime.Now;
}
