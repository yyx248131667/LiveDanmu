using System;
using System.IO;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LiveDanmuDesktop.Services;

public class CookieManager
{
	private class DouyinCookieConfig
	{
		public DouyinCookieData Cookie { get; set; } = new DouyinCookieData();

		public string LastUpdated { get; set; } = string.Empty;
	}

	private class DouyinCookieData
	{
		public string Douyin { get; set; } = string.Empty;
	}

	private class WeixinCookieConfig
	{
		public WeixinCookieData Cookie { get; set; } = new WeixinCookieData();

		public string LastUpdated { get; set; } = string.Empty;
	}

	private class WeixinCookieData
	{
		public string Weixin { get; set; } = string.Empty;
	}

	private readonly Logger _logger;

	private const string DouyinCookieFileName = "cookie_config.yaml";

	private const string WeixinCookieFileName = "weixin_cookie_config.yaml";

	private string? _cachedDouyinCookie;

	private string? _cachedWeixinCookie;

	private DateTime _lastDouyinLoad = DateTime.MinValue;

	private DateTime _lastWeixinLoad = DateTime.MinValue;

	private const int CacheTtlSeconds = 30;

	public CookieManager(Logger logger)
	{
		_logger = logger ?? throw new ArgumentNullException("logger");
	}

	public string? GetDouyinCookie(bool forceReload = false)
	{
		if (!forceReload && _cachedDouyinCookie != null && (DateTime.Now - _lastDouyinLoad).TotalSeconds < 30.0)
		{
			return _cachedDouyinCookie;
		}
		try
		{
			string? text = FindCookieFile("cookie_config.yaml");
			if (text == null)
			{
				_logger.Warn("[CookieManager] 未找到抖音 Cookie 文件: cookie_config.yaml");
				return null;
			}
			string input = File.ReadAllText(text, Encoding.UTF8);
			IDeserializer deserializer = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).IgnoreUnmatchedProperties().Build();
			_cachedDouyinCookie = deserializer.Deserialize<DouyinCookieConfig>(input)?.Cookie?.Douyin;
			_lastDouyinLoad = DateTime.Now;
			if (!string.IsNullOrWhiteSpace(_cachedDouyinCookie))
			{
				_logger.Info($"[CookieManager] 抖音 Cookie 已加载 ({_cachedDouyinCookie.Length} 字符) from {text}");
			}
			else
			{
				_logger.Warn("[CookieManager] 抖音 Cookie 文件存在但内容为空");
			}
			return _cachedDouyinCookie;
		}
		catch (Exception ex)
		{
			_logger.Error("[CookieManager] 加载抖音 Cookie 失败: " + ex.Message, ex);
			return null;
		}
	}

	public string? GetWeixinCookie(bool forceReload = false)
	{
		if (!forceReload && _cachedWeixinCookie != null && (DateTime.Now - _lastWeixinLoad).TotalSeconds < 30.0)
		{
			return _cachedWeixinCookie;
		}
		try
		{
			string? text = FindCookieFile("weixin_cookie_config.yaml");
			if (text == null)
			{
				_logger.Warn("[CookieManager] 未找到视频号 Cookie 文件: weixin_cookie_config.yaml");
				return null;
			}
			string input = File.ReadAllText(text, Encoding.UTF8);
			IDeserializer deserializer = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).IgnoreUnmatchedProperties().Build();
			_cachedWeixinCookie = deserializer.Deserialize<WeixinCookieConfig>(input)?.Cookie?.Weixin;
			_lastWeixinLoad = DateTime.Now;
			if (!string.IsNullOrWhiteSpace(_cachedWeixinCookie))
			{
				_logger.Info($"[CookieManager] 视频号 Cookie 已加载 ({_cachedWeixinCookie.Length} 字符) from {text}");
			}
			return _cachedWeixinCookie;
		}
		catch (Exception ex)
		{
			_logger.Error("[CookieManager] 加载视频号 Cookie 失败: " + ex.Message, ex);
			return null;
		}
	}

	public void InvalidateCache()
	{
		_cachedDouyinCookie = null;
		_cachedWeixinCookie = null;
		_lastDouyinLoad = DateTime.MinValue;
		_lastWeixinLoad = DateTime.MinValue;
		_logger.Info("[CookieManager] 缓存已清除");
	}

	private string? FindCookieFile(string fileName)
	{
		return AppPaths.FindFile(fileName, 6);
	}
}
