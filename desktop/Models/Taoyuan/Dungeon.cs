using System;
using System.Collections.Generic;

namespace LiveDanmuDesktop.Models.Taoyuan;

public class Dungeon
{
	public string Id { get; set; } = Guid.NewGuid().ToString();

	public string BossName { get; set; } = "野猪王";

	public int BossHp { get; set; } = 1000;

	public int BossMaxHp { get; set; } = 1000;

	public List<string> Participants { get; set; } = new List<string>();

	public bool IsActive { get; set; } = false;
}
