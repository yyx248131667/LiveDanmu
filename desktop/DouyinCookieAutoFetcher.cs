using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiveDanmuDesktop.Services;
using Microsoft.Web.WebView2.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LiveDanmuDesktop;

public class DouyinCookieAutoFetcher
{
	private class CookieValidationResult
	{
		public bool IsValid { get; set; }

		public string CookieString { get; set; } = string.Empty;

		public int CookieCount { get; set; }

		public List<string> KeyCookiesFound { get; set; } = new List<string>();
	}

	private class CookieConfig
	{
		public CookieData Cookie { get; set; } = new CookieData();

		public string LastUpdated { get; set; } = string.Empty;
	}

	private class CookieData
	{
		public string Douyin { get; set; } = string.Empty;
	}

	private readonly CoreWebView2 _coreWebView2;

	private readonly string _configPath;

	private readonly int _maxRetryCount;

	private readonly int _checkDelayMs;

	private int _currentRetryCount = 0;

	private CancellationTokenSource? _checkCancellationTokenSource;

	private readonly string[] _loginRequiredCookies = new string[4] { "sessionid", "sid_guard", "uid_tt", "sid_tt" };

	private readonly string[] _pageLoadCookies = new string[2] { "ttwid", "passport_csrf_token" };

	public event EventHandler<CookieFetchedEventArgs>? CookieFetched;

	public event EventHandler<CookieErrorEventArgs>? CookieError;

	public event EventHandler<string>? StatusUpdated;

	public DouyinCookieAutoFetcher(CoreWebView2 coreWebView2, string? configPath = null, int maxRetryCount = 3, int checkDelayMs = 2000)
	{
		_coreWebView2 = coreWebView2 ?? throw new ArgumentNullException("coreWebView2");
		_configPath = configPath ?? AppPaths.GetDataPath("cookie_config.yaml");
		_maxRetryCount = maxRetryCount;
		_checkDelayMs = checkDelayMs;
	}

	public void StartAutoMonitoring()
	{
		_coreWebView2.NavigationCompleted += OnNavigationCompleted;
		LogStatus("已启动 Cookie 自动监听");
	}

	public void StopAutoMonitoring()
	{
		_coreWebView2.NavigationCompleted -= OnNavigationCompleted;
		_checkCancellationTokenSource?.Cancel();
		LogStatus("已停止 Cookie 自动监听");
	}

	private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
	{
		if (!e.IsSuccess)
		{
			LogStatus($"页面加载失败: {e.WebErrorStatus}");
			return;
		}
		string currentUrl = _coreWebView2.Source;
		LogStatus("页面加载完成: " + currentUrl);
		if (IsDouyinPage(currentUrl))
		{
			LogStatus("检测到抖音页面，开始检查登录状态...");
			_checkCancellationTokenSource?.Cancel();
			_checkCancellationTokenSource = new CancellationTokenSource();
			await Task.Delay(_checkDelayMs, _checkCancellationTokenSource.Token);
			await StartLoginCheckLoop(_checkCancellationTokenSource.Token);
		}
	}

	private bool IsDouyinPage(string url)
	{
		return url.Contains("douyin.com") || url.Contains("live.douyin.com") || url.Contains("www.douyin.com");
	}

	private async Task StartLoginCheckLoop(CancellationToken cancellationToken)
	{
		_currentRetryCount = 0;
		while (_currentRetryCount < _maxRetryCount && !cancellationToken.IsCancellationRequested)
		{
			try
			{
				_currentRetryCount++;
				LogStatus($"第 {_currentRetryCount}/{_maxRetryCount} 次检查 Cookie...");
				CookieValidationResult cookieResult = await FetchAndValidateCookies();
				if (cookieResult.IsValid)
				{
					LogStatus("✓ 检测到有效 Cookie，开始保存...");
					await SaveCookieToFile(cookieResult.CookieString);
					this.CookieFetched?.Invoke(this, new CookieFetchedEventArgs
					{
						CookieString = cookieResult.CookieString,
						CookieCount = cookieResult.CookieCount,
						SavedPath = _configPath
					});
					LogStatus("✓ Cookie 已自动保存到: " + _configPath);
					return;
				}
				LogStatus($"未检测到有效 Cookie（当前 {cookieResult.CookieCount} 个），等待重试...");
				if (_currentRetryCount < _maxRetryCount)
				{
					await Task.Delay(_checkDelayMs, cancellationToken);
				}
			}
			catch (OperationCanceledException)
			{
				LogStatus("Cookie 检查已取消");
				return;
			}
			catch (Exception ex2)
			{
				LogStatus("❌ Cookie 检查异常: " + ex2.Message);
				Console.WriteLine($"[DouyinCookieAutoFetcher] 异常详情: {ex2}");
			}
		}
		if (_currentRetryCount >= _maxRetryCount)
		{
			string errorMsg = $"已达到最大重试次数 ({_maxRetryCount})，Cookie 获取失败";
			LogStatus("❌ " + errorMsg);
			this.CookieError?.Invoke(this, new CookieErrorEventArgs
			{
				ErrorMessage = errorMsg,
				RetryCount = _currentRetryCount
			});
		}
	}

	private async Task<CookieValidationResult> FetchAndValidateCookies()
	{
		try
		{
			CoreWebView2CookieManager cookieManager = _coreWebView2.CookieManager;
			string[] domains = new string[3] { "https://live.douyin.com", "https://www.douyin.com", "https://douyin.com" };
			List<CoreWebView2Cookie> allCookies = new List<CoreWebView2Cookie>();
			string[] array = domains;
			foreach (string domain in array)
			{
				allCookies.AddRange(await cookieManager.GetCookiesAsync(domain));
			}
			List<CoreWebView2Cookie> uniqueCookies = (from c in allCookies
				group c by c.Name + "_" + c.Domain into g
				select g.First()).ToList();
			string cookieString = string.Join("; ", uniqueCookies.Select((CoreWebView2Cookie c) => c.Name + "=" + c.Value));
			List<string> loginCookiesFound = (from c in uniqueCookies
				where _loginRequiredCookies.Contains<string>(c.Name, StringComparer.OrdinalIgnoreCase)
				select c.Name).ToList();
			bool hasLoginCookie = loginCookiesFound.Count > 0;
			bool hasValidContent = !string.IsNullOrWhiteSpace(cookieString) && cookieString.Length > 50;
			if (loginCookiesFound.Count > 0)
			{
				Console.WriteLine("[DouyinCookieAutoFetcher] 检测到登录 Cookie: " + string.Join(", ", loginCookiesFound));
			}
			return new CookieValidationResult
			{
				IsValid = (hasLoginCookie && hasValidContent),
				CookieString = cookieString,
				CookieCount = uniqueCookies.Count,
				KeyCookiesFound = loginCookiesFound
			};
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Console.WriteLine($"[DouyinCookieAutoFetcher] 获取 Cookie 失败: {ex2}");
			return new CookieValidationResult
			{
				IsValid = false
			};
		}
	}

	private async Task SaveCookieToFile(string cookieString)
	{
		try
		{
			CookieConfig config;
			if (File.Exists(_configPath))
			{
				string existingYaml = await File.ReadAllTextAsync(_configPath, Encoding.UTF8);
				IDeserializer deserializer = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).Build();
				config = deserializer.Deserialize<CookieConfig>(existingYaml) ?? new CookieConfig();
			}
			else
			{
				config = new CookieConfig();
			}
			config.Cookie.Douyin = cookieString;
			config.LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
			ISerializer serializer = new SerializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).Build();
			string yaml = serializer.Serialize(config);
			string? directory = Path.GetDirectoryName(_configPath);
			if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
			{
				Directory.CreateDirectory(directory);
			}
			await File.WriteAllTextAsync(_configPath, yaml, Encoding.UTF8);
			Console.WriteLine("[DouyinCookieAutoFetcher] Cookie 已保存到: " + _configPath);
			List<string> extraPaths = new List<string>();
			string cwdPath = Path.Combine(Environment.CurrentDirectory, "cookie_config.yaml");
			if (cwdPath != _configPath)
			{
				extraPaths.Add(cwdPath);
			}
			DirectoryInfo searchDir = new DirectoryInfo(AppPaths.AppDataRoot);
			for (int i = 0; i < 6; i++)
			{
				if (searchDir?.Parent == null)
				{
					break;
				}
				searchDir = searchDir.Parent;
				if (File.Exists(Path.Combine(searchDir.FullName, "go.mod")) || Directory.Exists(Path.Combine(searchDir.FullName, ".git")))
				{
					string rootPath = Path.Combine(searchDir.FullName, "cookie_config.yaml");
					if (rootPath != _configPath)
					{
						extraPaths.Add(rootPath);
					}
					break;
				}
			}
			foreach (string extraPath in extraPaths)
			{
				try
				{
					await File.WriteAllTextAsync(extraPath, yaml, Encoding.UTF8);
					Console.WriteLine("[DouyinCookieAutoFetcher] Cookie 同步到: " + extraPath);
				}
				catch (Exception ex)
				{
					Console.WriteLine("[DouyinCookieAutoFetcher] 同步 Cookie 到 " + extraPath + " 失败: " + ex.Message);
				}
			}
		}
		catch (Exception ex2)
		{
			Exception ex3 = ex2;
			throw new Exception("保存 Cookie 失败: " + ex3.Message, ex3);
		}
	}

	public async Task<bool> ManualFetchCookie()
	{
		try
		{
			LogStatus("手动获取 Cookie...");
			CookieValidationResult cookieResult = await FetchAndValidateCookies();
			if (cookieResult.IsValid)
			{
				await SaveCookieToFile(cookieResult.CookieString);
				this.CookieFetched?.Invoke(this, new CookieFetchedEventArgs
				{
					CookieString = cookieResult.CookieString,
					CookieCount = cookieResult.CookieCount,
					SavedPath = _configPath
				});
				LogStatus($"✓ Cookie 已保存 ({cookieResult.CookieCount} 个)");
				return true;
			}
			LogStatus("❌ 未检测到有效 Cookie");
			return false;
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			LogStatus("❌ 获取失败: " + ex2.Message);
			return false;
		}
	}

	public static async Task<string?> LoadCookieFromFile(string? configPath = null)
	{
		try
		{
			string path = configPath ?? AppPaths.GetDataPath("cookie_config.yaml");
			if (!File.Exists(path))
			{
				Console.WriteLine("[DouyinCookieAutoFetcher] 配置文件不存在: " + path);
				return null;
			}
			string yaml = await File.ReadAllTextAsync(path, Encoding.UTF8);
			IDeserializer deserializer = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).Build();
			return deserializer.Deserialize<CookieConfig>(yaml)?.Cookie?.Douyin;
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Console.WriteLine($"[DouyinCookieAutoFetcher] 加载 Cookie 失败: {ex2}");
			return null;
		}
	}

	private void LogStatus(string message)
	{
		Console.WriteLine("[DouyinCookieAutoFetcher] " + message);
		this.StatusUpdated?.Invoke(this, message);
	}
}
