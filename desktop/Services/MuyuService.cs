using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using LiveDanmuDesktop.Models;

namespace LiveDanmuDesktop.Services;

public class MuyuService : IDisposable
{
	private readonly MuyuConfigService _configService;

	private MuyuConfig _config;

	private readonly Logger _logger;

	private readonly Dictionary<string, int> _totalCounts = new Dictionary<string, int>
	{
		["douyin"] = 0,
		["weixin"] = 0
	};

	private readonly Dictionary<string, int> _likeCounts = new Dictionary<string, int>
	{
		["douyin"] = 0,
		["weixin"] = 0
	};

	private readonly Dictionary<string, int> _giftCounts = new Dictionary<string, int>
	{
		["douyin"] = 0,
		["weixin"] = 0
	};

	private readonly Dictionary<string, int> _lastLikeCountTotal = new Dictionary<string, int>
	{
		["douyin"] = 0,
		["weixin"] = 0
	};

	private readonly Dictionary<string, Dictionary<string, string>> _collectedGifts = new Dictionary<string, Dictionary<string, string>>
	{
		["douyin"] = new Dictionary<string, string>(),
		["weixin"] = new Dictionary<string, string>()
	};

	public int TotalCount
	{
		get
		{
			return _totalCounts.GetValueOrDefault(ActivePlatform, 0);
		}
		private set
		{
			_totalCounts[ActivePlatform] = value;
		}
	}

	public int LikeCount
	{
		get
		{
			return _likeCounts.GetValueOrDefault(ActivePlatform, 0);
		}
		private set
		{
			_likeCounts[ActivePlatform] = value;
		}
	}

	public int GiftCount
	{
		get
		{
			return _giftCounts.GetValueOrDefault(ActivePlatform, 0);
		}
		private set
		{
			_giftCounts[ActivePlatform] = value;
		}
	}

	public string ActivePlatform { get; private set; } = "douyin";

	public bool SuppressMuyuHit { get; set; } = false;

	public event EventHandler<MuyuHitEventArgs>? MuyuHit;

	public event EventHandler<MuyuConfigEventArgs>? ConfigChanged;

	public event EventHandler? CounterReset;

	public event EventHandler<GiftCollectedEventArgs>? GiftCollected;

	public event EventHandler<DanmakuReceivedEventArgs>? DanmakuReceived;

	public event EventHandler<ViewerCountUpdatedEventArgs>? ViewerCountUpdated;

	public int GetTotalCount(string platform)
	{
		return _totalCounts.GetValueOrDefault(platform, 0);
	}

	public int GetLikeCount(string platform)
	{
		return _likeCounts.GetValueOrDefault(platform, 0);
	}

	public int GetGiftCount(string platform)
	{
		return _giftCounts.GetValueOrDefault(platform, 0);
	}

	public MuyuService(MuyuConfigService configService, Logger? logger = null)
	{
		_configService = configService;
		_config = _configService.Load();
		_logger = logger ?? new Logger();
		_logger.Info($"[MuyuService] 初始化完成, Douyin.TriggerLike={_config.Douyin.TriggerLike}, Douyin.LikeRate={_config.Douyin.LikeRate}");
	}

	public PlatformMuyuConfig GetConfig(string platform)
	{
		return (platform == "weixin") ? _config.Weixin : _config.Douyin;
	}

	public void SwitchPlatform(string platform)
	{
		ActivePlatform = platform;
		this.ConfigChanged?.Invoke(this, new MuyuConfigEventArgs
		{
			Platform = platform,
			Config = GetConfig(platform)
		});
	}

	public void SaveConfig(string platform, PlatformMuyuConfig config)
	{
		if (platform == "weixin")
		{
			_config.Weixin = config;
		}
		else
		{
			_config.Douyin = config;
		}
		_configService.Save(_config);
		this.ConfigChanged?.Invoke(this, new MuyuConfigEventArgs
		{
			Platform = platform,
			Config = config
		});
	}

	public void SetSkin(string platform, string skinType, string? imageData)
	{
		PlatformMuyuConfig config = GetConfig(platform);
		config.Skin = skinType;
		config.CustomSkinData = ((skinType == "custom") ? imageData : null);
		SaveConfig(platform, config);
	}

	public void ResetCounters()
	{
		_totalCounts["douyin"] = 0;
		_totalCounts["weixin"] = 0;
		_likeCounts["douyin"] = 0;
		_likeCounts["weixin"] = 0;
		_giftCounts["douyin"] = 0;
		_giftCounts["weixin"] = 0;
		_lastLikeCountTotal["douyin"] = 0;
		_lastLikeCountTotal["weixin"] = 0;
		this.CounterReset?.Invoke(this, EventArgs.Empty);
	}

	public IReadOnlyDictionary<string, string> GetCollectedGifts(string platform)
	{
		Dictionary<string, string>? value;
		return _collectedGifts.TryGetValue(platform, out value) ? value : new Dictionary<string, string>();
	}

	public void ProcessMessage(LiveMessage message)
	{
		string text = NormalizePlatform(message.Platform);
		_logger.Debug($"[MuyuService] ProcessMessage: MsgType={message.MsgType}, Method={message.Method}, Platform={text}");
		int num = ExtractViewerCount(message);
		if (num > 0)
		{
			this.ViewerCountUpdated?.Invoke(this, new ViewerCountUpdatedEventArgs
			{
				Platform = text,
				Count = num
			});
		}
		if (message.MsgType == "like_count")
		{
			HandleLikeCount(message, text);
			return;
		}
		switch (message.Method)
		{
		case "WebcastChatMessage":
			HandleDanmaku(message, text);
			break;
		case "WebcastGiftMessage":
			HandleGift(message, text);
			break;
		case "WebcastLikeMessage":
			HandleLike(message, text);
			break;
		case "WebcastSocialMessage":
			HandleFollow(message, text);
			break;
		case "WebcastMemberMessage":
			HandleEnter(message, text);
			break;
		case "viewer_count":
			break;
		case "WebcastRoomUserSeqMessage":
			break;
		default:
			_logger.Debug("[MuyuService] 未匹配方法: " + message.Method);
			break;
		}
	}

	private static string NormalizePlatform(string platform)
	{
		return platform.Contains("weixin") ? "weixin" : "douyin";
	}

	private static int ExtractViewerCount(LiveMessage message)
	{
		if (message.ExtraData == null)
		{
			return 0;
		}
		string[] array = new string[4] { "viewer_count", "viewerCount", "online_count", "member_count" };
		foreach (string key in array)
		{
			if (message.ExtraData.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var value2))
			{
				return value2;
			}
		}
		return 0;
	}

	private void HandleDanmaku(LiveMessage message, string platform)
	{
		PlatformMuyuConfig config = GetConfig(platform);
		this.DanmakuReceived?.Invoke(this, new DanmakuReceivedEventArgs
		{
			MsgType = "chat",
			Platform = platform,
			User = message.Username,
			Content = message.Content
		});
		if (config.TriggerDanmaku)
		{
			int num = CalculateDanmakuHits(message.Content, config.DanmakuRules);
			if (num > 0)
			{
				_totalCounts[platform] = _totalCounts.GetValueOrDefault(platform, 0) + num;
				EmitMuyuHit(num, config, platform, message.Username, "弹幕 " + message.Username + ": " + message.Content, "WebcastChatMessage");
			}
		}
	}

	internal static int CalculateDanmakuHits(string content, Dictionary<string, int> rules)
	{
		if (rules.TryGetValue(content, out var value))
		{
			return value;
		}
		if (rules.TryGetValue("其他", out var value2))
		{
			return value2;
		}
		return 0;
	}

	private void HandleGift(LiveMessage message, string platform)
	{
		PlatformMuyuConfig config = GetConfig(platform);
		string giftId = ExtractGiftId(message);
		string text = ExtractGiftName(message);
		int num = ExtractGiftCount(message);
		this.DanmakuReceived?.Invoke(this, new DanmakuReceivedEventArgs
		{
			MsgType = "gift",
			Platform = platform,
			User = message.Username,
			Content = $"送出 {text} x{num}"
		});
		CollectGift(platform, giftId, text);
		if (config.TriggerGift && IsGiftMatch(config.GiftSelect, giftId, text))
		{
			int giftRuleValue = GetGiftRuleValue(platform, config, text);
			int num2 = Math.Min(giftRuleValue * num, 50);
			if (num2 > 0)
			{
				_totalCounts[platform] = _totalCounts.GetValueOrDefault(platform, 0) + num2;
				_giftCounts[platform] = _giftCounts.GetValueOrDefault(platform, 0) + num2;
				EmitMuyuHit(num2, config, platform, message.Username, $"礼物 {message.Username}: {text} x{num}", "WebcastGiftMessage");
			}
		}
	}

	internal static string ExtractGiftId(LiveMessage message)
	{
		string? text = TryExtractStringField(message.ExtraData, "giftId", "gift_id", "giftID");
		if (string.IsNullOrEmpty(text) && message.ExtraData != null && message.ExtraData.TryGetValue("gift", out var value) && value.ValueKind == JsonValueKind.Object && value.TryGetProperty("id", out var value2))
		{
			text = JsonElementToString(value2);
		}
		if (string.IsNullOrEmpty(text) || text == "0")
		{
			text = ExtractGiftName(message);
		}
		return string.IsNullOrEmpty(text) ? "unknown" : text;
	}

	internal static string ExtractGiftName(LiveMessage message)
	{
		return TryExtractStringField(message.ExtraData, "giftName", "gift_name", "giftname") ?? "";
	}

	internal static int ExtractGiftCount(LiveMessage message)
	{
		if (message.ExtraData == null)
		{
			return 1;
		}
		string[] array = new string[5] { "giftCount", "gift_count", "repeatCount", "repeat_count", "comboCount" };
		foreach (string key in array)
		{
			if (message.ExtraData.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var value2))
			{
				return Math.Max(value2, 1);
			}
		}
		return 1;
	}

	internal static bool IsGiftMatch(string giftSelect, string giftId)
	{
		return giftSelect == "all" || giftSelect == giftId;
	}

	internal static bool IsGiftMatch(string giftSelect, string giftId, string giftName)
	{
		return giftSelect == "all" ||
			string.Equals(giftSelect, giftId, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(NormalizeGiftKey(giftSelect), NormalizeGiftKey(giftName), StringComparison.OrdinalIgnoreCase);
	}

	private static string NormalizeGiftKey(string value) =>
		string.Concat(value.Where(ch => !char.IsWhiteSpace(ch)));

	internal static int GetGiftRuleValue(string platform, PlatformMuyuConfig cfg, string giftName)
	{
		if (platform == "weixin")
		{
			return cfg.GiftRate;
		}
		if (cfg.GiftRules.TryGetValue(giftName, out var value))
		{
			return value;
		}
		if (cfg.GiftRules.TryGetValue("其他", out var value2))
		{
			return value2;
		}
		return 1;
	}

	private void CollectGift(string platform, string giftId, string giftName)
	{
		if (!_collectedGifts.ContainsKey(platform))
		{
			_collectedGifts[platform] = new Dictionary<string, string>();
		}
		Dictionary<string, string> dictionary = _collectedGifts[platform];
		if (!dictionary.ContainsKey(giftId))
		{
			dictionary[giftId] = giftName;
			this.GiftCollected?.Invoke(this, new GiftCollectedEventArgs
			{
				Platform = platform,
				GiftId = giftId,
				GiftName = giftName,
				TotalGifts = dictionary.Count
			});
		}
	}

	private void HandleLike(LiveMessage message, string platform)
	{
		PlatformMuyuConfig config = GetConfig(platform);
		int likeCount = ExtractLikeCount(message);
		_logger.Info($"[MuyuService] HandleLike: platform={platform}, user={message.Username}, TriggerLike={config.TriggerLike}, LikeRate={config.LikeRate}");
		this.DanmakuReceived?.Invoke(this, new DanmakuReceivedEventArgs
		{
			MsgType = "like",
			Platform = platform,
			User = message.Username,
			Content = $"点赞 x{likeCount}"
		});
		if (!config.TriggerLike)
		{
			_logger.Info("[MuyuService] 点赞触发已禁用，跳过");
			return;
		}
		int likeRate = config.LikeRate;
		if (likeRate <= 0)
		{
			_logger.Info("[MuyuService] 点赞倍率为0，跳过");
			return;
		}
		int hits = (int)Math.Min((long)likeCount * likeRate, int.MaxValue);
		_totalCounts[platform] = _totalCounts.GetValueOrDefault(platform, 0) + hits;
		_likeCounts[platform] = _likeCounts.GetValueOrDefault(platform, 0) + hits;
		Logger logger = _logger;
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(53, 3);
		defaultInterpolatedStringHandler.AppendLiteral("[MuyuService] ★ 触发木鱼: hits=");
		defaultInterpolatedStringHandler.AppendFormatted(hits);
		defaultInterpolatedStringHandler.AppendLiteral(", totalCount=");
		defaultInterpolatedStringHandler.AppendFormatted(_totalCounts[platform]);
		defaultInterpolatedStringHandler.AppendLiteral(", MuyuHit订阅者=");
		EventHandler<MuyuHitEventArgs>? eventHandler = this.MuyuHit;
		defaultInterpolatedStringHandler.AppendFormatted((eventHandler != null) ? eventHandler.GetInvocationList().Length : 0);
		logger.Info(defaultInterpolatedStringHandler.ToStringAndClear());
		EmitMuyuHit(hits, config, platform, message.Username, $"点赞 {message.Username} x{likeCount}", "WebcastLikeMessage");
	}

	internal static int ExtractLikeCount(LiveMessage message)
	{
		if (message.ExtraData != null)
		{
			string[] keys = { "likeCount", "like_count", "count", "comboCount" };
			foreach (string key in keys)
			{
				if (!message.ExtraData.TryGetValue(key, out var value)) continue;
				if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
					return Math.Max(number, 1);
				if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number))
					return Math.Max(number, 1);
			}
		}
		return 1;
	}

	private void HandleLikeCount(LiveMessage message, string platform)
	{
		if (!int.TryParse(message.Content.Trim(), out var result))
		{
			return;
		}
		int valueOrDefault = _lastLikeCountTotal.GetValueOrDefault(platform, 0);
		Console.WriteLine($"[MuyuService] HandleLikeCount: platform={platform}, currentTotal={result}, lastTotal={valueOrDefault}");
		if (valueOrDefault == 0)
		{
			_lastLikeCountTotal[platform] = result;
			Console.WriteLine($"[MuyuService] 首次收到点赞数，记录基准值: {result}");
			return;
		}
		int num = result - valueOrDefault;
		_lastLikeCountTotal[platform] = result;
		Console.WriteLine($"[MuyuService] 点赞增量: delta={num}");
		if (num <= 0)
		{
			return;
		}
		PlatformMuyuConfig config = GetConfig(platform);
		if (!config.TriggerLike)
		{
			Console.WriteLine("[MuyuService] 点赞触发已禁用，跳过");
			return;
		}
		int num2 = num * config.LikeRate;
		Console.WriteLine($"[MuyuService] 计算木鱼数: delta={num} × LikeRate={config.LikeRate} = {num2}");
		if (num2 > 0)
		{
			_totalCounts[platform] = _totalCounts.GetValueOrDefault(platform, 0) + num2;
			_likeCounts[platform] = _likeCounts.GetValueOrDefault(platform, 0) + num2;
			Console.WriteLine($"[MuyuService] 触发木鱼: hits={num2}, totalCount={_totalCounts[platform]}");
			EmitMuyuHit(num2, config, platform, "点赞", $"点赞 +{num} (总计 {result})", "WebcastLikeMessage");
		}
	}

	private void HandleEnter(LiveMessage message, string platform)
	{
		PlatformMuyuConfig config = GetConfig(platform);
		this.DanmakuReceived?.Invoke(this, new DanmakuReceivedEventArgs
		{
			MsgType = "member",
			Platform = platform,
			User = message.Username,
			Content = "进入直播间"
		});
		if (!(platform != "douyin") && config.TriggerEnter)
		{
			int enterRate = config.EnterRate;
			if (enterRate > 0)
			{
				_totalCounts[platform] = _totalCounts.GetValueOrDefault(platform, 0) + enterRate;
				EmitMuyuHit(enterRate, config, platform, message.Username, "进场 " + message.Username, "WebcastMemberMessage");
			}
		}
	}

	private void HandleFollow(LiveMessage message, string platform)
	{
		PlatformMuyuConfig config = GetConfig(platform);
		this.DanmakuReceived?.Invoke(this, new DanmakuReceivedEventArgs
		{
			MsgType = "social",
			Platform = platform,
			User = message.Username,
			Content = "关注了主播"
		});
		if (!(platform != "douyin") && config.TriggerFollow)
		{
			int followRate = config.FollowRate;
			if (followRate > 0)
			{
				_totalCounts[platform] = _totalCounts.GetValueOrDefault(platform, 0) + followRate;
				EmitMuyuHit(followRate, config, platform, message.Username, "关注 " + message.Username, "WebcastSocialMessage");
			}
		}
	}

	private void EmitMuyuHit(int hits, PlatformMuyuConfig cfg, string platform, string username, string logContent, string method = "")
	{
		if (SuppressMuyuHit)
		{
			return;
		}
		(int likeRate, int giftRate) platformRates = GetPlatformRates(platform, cfg);
		int item = platformRates.likeRate;
		int item2 = platformRates.giftRate;
		MuyuHitEventArgs e = new MuyuHitEventArgs
		{
			Hits = hits,
			Text = cfg.Text,
			User = username,
			LogContent = logContent,
			Platform = platform,
			Method = method,
			TotalCount = GetTotalCount(platform),
			LikeCount = GetLikeCount(platform),
			GiftCount = GetGiftCount(platform),
			PlaySound = (cfg.SoundEnabled && !cfg.Mute),
			Volume = cfg.Volume,
			AudioSpeed = cfg.AudioSpeed,
			LikeRate = item,
			GiftRate = item2
		};
		EventHandler<MuyuHitEventArgs>? eventHandler = this.MuyuHit;
		if (eventHandler == null)
		{
			_logger.Error("[MuyuService] ❌ MuyuHit 事件没有订阅者！无人监听木鱼事件");
			return;
		}
		try
		{
			_logger.Info($"[MuyuService] ✅ EmitMuyuHit: 触发 MuyuHit 事件, hits={hits}, platform={platform}, subscribers={eventHandler.GetInvocationList().Length}");
			eventHandler(this, e);
			_logger.Info("[MuyuService] ✅ MuyuHit 事件触发完成");
		}
		catch (Exception ex)
		{
			_logger.Error("[MuyuService] ❌ MuyuHit 事件触发失败: " + ex.Message, ex);
		}
	}

	internal static (int likeRate, int giftRate) GetPlatformRates(string platform, PlatformMuyuConfig cfg)
	{
		if (platform == "weixin")
		{
			return (likeRate: cfg.LikeRate, giftRate: cfg.GiftRate);
		}
		int value;
		int item = ((!cfg.GiftRules.TryGetValue("其他", out value)) ? 1 : value);
		return (likeRate: cfg.LikeRate, giftRate: item);
	}

	private static string? TryExtractStringField(Dictionary<string, JsonElement>? data, params string[] keys)
	{
		if (data == null)
		{
			return null;
		}
		foreach (string key in keys)
		{
			if (data.TryGetValue(key, out var value))
			{
				string? text = JsonElementToString(value);
				if (!string.IsNullOrEmpty(text))
				{
					return text;
				}
			}
		}
		return null;
	}

	private static string? JsonElementToString(JsonElement el)
	{
		JsonValueKind valueKind = el.ValueKind;
		if (1 == 0)
		{
		}
		string? result = valueKind switch
		{
			JsonValueKind.String => el.GetString(), 
			JsonValueKind.Number => el.GetRawText(), 
			_ => null, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	public void Dispose()
	{
		this.MuyuHit = null;
		this.ConfigChanged = null;
		this.CounterReset = null;
		this.GiftCollected = null;
		this.DanmakuReceived = null;
		this.ViewerCountUpdated = null;
	}
}
