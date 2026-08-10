using System;

namespace LiveDanmuDesktop.Models.Taoyuan;

public class Crop
{
	public string Id { get; set; } = Guid.NewGuid().ToString();

	public string OwnerName { get; set; } = "";

	public string Type { get; set; } = "小麦";

	public DateTime PlantTime { get; set; } = DateTime.Now;

	public int GrowTimeSeconds { get; set; } = 30;

	public bool IsMature => (DateTime.Now - PlantTime).TotalSeconds >= (double)GrowTimeSeconds;

	public double Progress => Math.Min(1.0, (DateTime.Now - PlantTime).TotalSeconds / (double)GrowTimeSeconds);
}
