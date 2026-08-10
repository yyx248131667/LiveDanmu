using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiveDanmuDesktop.Models;

public class MuyuConfig
{
	public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.Never,
		WriteIndented = true
	};

	public PlatformMuyuConfig Douyin { get; set; } = new PlatformMuyuConfig();

	public PlatformMuyuConfig Weixin { get; set; } = new PlatformMuyuConfig();
}
