using System;
using System.Collections.Generic;

namespace LiveDanmuDesktop.Models.Taoyuan;

public class Team
{
	public string Id { get; set; } = Guid.NewGuid().ToString();

	public string Name { get; set; } = "";

	public List<string> MemberNames { get; set; } = new List<string>();

	public int Level { get; set; } = 1;
}
