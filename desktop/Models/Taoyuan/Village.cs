using System.Collections.Generic;

namespace LiveDanmuDesktop.Models.Taoyuan;

public class Village
{
	public int Level { get; set; } = 1;

	public List<Crop> Crops { get; set; } = new List<Crop>();
}
