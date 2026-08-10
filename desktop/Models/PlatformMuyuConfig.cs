using System.Collections.Generic;

namespace LiveDanmuDesktop.Models;

public class PlatformMuyuConfig
{
	public string Text { get; set; } = "好运连连";

	public string Skin { get; set; } = "muyu";

	public string? CustomSkinData { get; set; }

	public bool GreenScreen { get; set; } = false;

	public bool SoundEnabled { get; set; } = true;

	public bool TriggerDanmaku { get; set; } = true;

	public bool TriggerGift { get; set; } = true;

	public bool TriggerLike { get; set; } = true;

	public bool TriggerEnter { get; set; } = true;

	public bool TriggerFollow { get; set; } = true;

	public int LikeRate { get; set; } = 1;

	public int EnterRate { get; set; } = 1;

	public int FollowRate { get; set; } = 1;

	public int GiftRate { get; set; } = 1;

	public string GiftSelect { get; set; } = "all";

	public Dictionary<string, int> GiftRules { get; set; } = new Dictionary<string, int> { ["其他"] = 1 };

	public Dictionary<string, int> DanmakuRules { get; set; } = new Dictionary<string, int> { ["其他"] = 1 };

	public string BubbleText { get; set; } = "{用户} {文本}";

	public string TextColor { get; set; } = "#ffffff";

	public string NumColor { get; set; } = "#e8312a";

	public string TextLayer { get; set; } = "above";

	public string GiftMode { get; set; } = "separate";

	public int AudioSpeed { get; set; } = 100;

	public int Volume { get; set; } = 80;

	public bool Mute { get; set; } = false;
}
