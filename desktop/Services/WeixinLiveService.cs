using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Threading;
using LiveDanmuDesktop.Models;
using Microsoft.Web.WebView2.Core;

namespace LiveDanmuDesktop.Services;

public class WeixinLiveService : IDisposable
{
	private readonly MessageAggregator _messageAggregator;

	private readonly Logger _logger;

	private readonly CookieManager _cookieManager;

	private Window? _hostWindow;

	private WebView2? _webView;

	private CoreWebView2? _coreWebView2;

	private bool _disposed;

	private bool _isMonitoring;

	private bool _isLoginMode;

	private CancellationTokenSource? _cancellationTokenSource;

	private Timer? _healthCheckTimer;

	private Timer? _periodicRefreshTimer;

	private DateTime _lastResponseTime = DateTime.MinValue;

	private const string ControlPanelUrl = "https://channels.weixin.qq.com/platform/live/liveBuild";

	public bool IsRunning => _isMonitoring;

	public event EventHandler<string>? StatusChanged;

	public async Task RequestLoginAsync()
	{
		if (_isMonitoring && _coreWebView2 != null)
		{
			Dispatcher.UIThread.Post(delegate
			{
				_coreWebView2.Navigate("https://channels.weixin.qq.com/platform/live/liveBuild");
			});
			ShowLoginWindow();
		}
		else
		{
			await StartAsync("");
		}
	}

	public WeixinLiveService(MessageAggregator messageAggregator, Logger logger, CookieManager cookieManager)
	{
		_messageAggregator = messageAggregator ?? throw new ArgumentNullException("messageAggregator");
		_logger = logger ?? throw new ArgumentNullException("logger");
		_cookieManager = cookieManager ?? throw new ArgumentNullException("cookieManager");
	}

	public async Task StartAsync(string roomId, bool headless = false)
	{
		if (_isMonitoring)
		{
			_logger.Warn("[WeixinLive] 服务已在运行中");
			return;
		}
		_cancellationTokenSource = new CancellationTokenSource();
		try
		{
			_logger.Info("[WeixinLive] 启动视频号服务，房间: " + roomId);
			this.StatusChanged?.Invoke(this, "正在初始化 WebView2...");
			TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
			Dispatcher.UIThread.Post(async delegate
			{
				try
				{
					await InitializeWebView2Async();
					tcs.TrySetResult(result: true);
				}
				catch (Exception ex3)
				{
					Exception ex4 = ex3;
					tcs.TrySetException(ex4);
				}
			});
			await tcs.Task;
			_isMonitoring = true;
			_lastResponseTime = DateTime.Now;
			this.StatusChanged?.Invoke(this, "已连接 - 正在监听弹幕");
			StartHealthChecks();
			_logger.Info("[WeixinLive] 视频号服务启动成功");
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			_logger.Error("[WeixinLive] 启动失败: " + ex2.Message, ex2);
			this.StatusChanged?.Invoke(this, "启动失败: " + ex2.Message);
			await StopAsync();
			throw;
		}
	}

	private async Task InitializeWebView2Async()
	{
		_hostWindow = new Window
		{
			Title = "微信视频号登录 - 正在初始化...",
			Width = 520.0,
			Height = 680.0,
			ShowInTaskbar = true,
			WindowStartupLocation = WindowStartupLocation.CenterScreen,
			CanResize = true
		};
		_webView = new WebView2
		{
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Stretch
		};
		Grid panel = new Grid
		{
			Children = { (Control)_webView }
		};
		_hostWindow.Content = panel;
		_hostWindow.Show();
		CoreWebView2Environment env = await AppPaths.CreateWebView2EnvironmentAsync("weixin-service");
		await _webView.EnsureCoreWebView2Async(env);
		_coreWebView2 = _webView.CoreWebView2;
		if (_coreWebView2 == null)
		{
			throw new InvalidOperationException("WebView2 核心初始化失败");
		}
		CoreWebView2Settings settings = _coreWebView2.Settings;
		settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
		settings.IsScriptEnabled = true;
		settings.AreDefaultScriptDialogsEnabled = true;
		settings.IsWebMessageEnabled = true;
		_logger.Info("[WeixinLive] WebView2 初始化成功");
		await _coreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("Object.defineProperty(navigator, 'webdriver', { get: () => false });");
		_coreWebView2.WebResourceResponseReceived += OnWebResourceResponseReceived;
		_coreWebView2.NavigationCompleted += OnNavigationCompleted;
		_logger.Info("[WeixinLive] 导航到: https://channels.weixin.qq.com/platform/live/liveBuild");
		await InjectCookiesAsync();
		_coreWebView2.Navigate("https://channels.weixin.qq.com/platform/live/liveBuild");
	}

	private async Task InjectCookiesAsync()
	{
		if (_coreWebView2 == null)
		{
			return;
		}
		string? cookieString = _cookieManager.GetWeixinCookie();
		if (string.IsNullOrWhiteSpace(cookieString))
		{
			_logger.Warn("[WeixinLive] 未找到视频号 Cookie，可能需要先登录");
			return;
		}
		CoreWebView2CookieManager cookieManager = _coreWebView2.CookieManager;
		string[] cookiePairs = cookieString.Split(new string[1] { "; " }, StringSplitOptions.RemoveEmptyEntries);
		string[] array = cookiePairs;
		foreach (string pair in array)
		{
			int idx = pair.IndexOf('=');
			if (idx > 0)
			{
				string name = pair.Substring(0, idx).Trim();
				string value = pair.Substring(idx + 1).Trim();
				try
				{
					CoreWebView2Cookie cookie = cookieManager.CreateCookie(name, value, ".weixin.qq.com", "/");
					cookieManager.AddOrUpdateCookie(cookie);
					CoreWebView2Cookie channelsCookie = cookieManager.CreateCookie(name, value, "channels.weixin.qq.com", "/");
					cookieManager.AddOrUpdateCookie(channelsCookie);
				}
				catch (Exception ex)
				{
					Exception ex2 = ex;
					_logger.Debug("[WeixinLive] 设置 Cookie 失败: " + name + " - " + ex2.Message);
				}
			}
		}
		_logger.Info($"[WeixinLive] 已注入 {cookiePairs.Length} 个 Cookie");
	}

	private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
	{
		string text = _coreWebView2?.Source ?? "";
		_logger.Info("[WeixinLive] 页面加载完成: " + text);
		if (text.Contains("login"))
		{
			_logger.Warn("[WeixinLive] 页面跳转到登录页，需要扫码登录");
			this.StatusChanged?.Invoke(this, "需要登录 - 正在打开扫码窗口...");
			ShowLoginWindow();
		}
		else if (text.Contains("liveBuild") || text.Contains("platform"))
		{
			if (_isLoginMode)
			{
				_logger.Info("[WeixinLive] 登录成功，隐藏浏览器窗口");
				HideToPixel();
			}
			this.StatusChanged?.Invoke(this, "已连接 - 正在监听弹幕");
			_ = InjectHelperScriptAsync();
		}
	}

	private void ShowLoginWindow()
	{
		Dispatcher.UIThread.Post(delegate
		{
			if (_hostWindow != null)
			{
				_isLoginMode = true;
				_hostWindow.SystemDecorations = SystemDecorations.Full;
				_hostWindow.Opacity = 1.0;
				_hostWindow.Width = 520.0;
				_hostWindow.Height = 680.0;
				_hostWindow.Title = "微信视频号登录 - 请扫码";
				_hostWindow.ShowInTaskbar = true;
				_hostWindow.CanResize = true;
				_hostWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
				Screen? primary = _hostWindow.Screens.Primary;
				if (primary != null)
				{
					PixelRect workingArea = primary.WorkingArea;
					int x = (workingArea.Width - 520) / 2 + workingArea.X;
					int y = (workingArea.Height - 680) / 2 + workingArea.Y;
					_hostWindow.Position = new PixelPoint(x, y);
				}
				_hostWindow.Topmost = true;
				_logger.Info("[WeixinLive] 扫码登录窗口已打开");
			}
		});
	}

	private void HideToPixel()
	{
		Dispatcher.UIThread.Post(delegate
		{
			if (_hostWindow != null)
			{
				_isLoginMode = false;
				_hostWindow.Topmost = false;
				_hostWindow.ShowInTaskbar = false;
				_hostWindow.Title = "视频号监控 (后台)";
				_hostWindow.SystemDecorations = SystemDecorations.None;
				_hostWindow.Width = 1.0;
				_hostWindow.Height = 1.0;
				_hostWindow.Opacity = 0.0;
				_hostWindow.Position = new PixelPoint(-100, -100);
				_hostWindow.CanResize = false;
				_logger.Info("[WeixinLive] 浏览器窗口已隐藏 (1px 模式)");
			}
		});
	}

	private async void OnWebResourceResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
	{
		try
		{
			string url = e.Request.Uri;
			if (!url.Contains("mmfinderassistant-bin/live/msg"))
			{
				return;
			}
			_logger.Debug("[WeixinLive] 拦截到消息 API: " + url);
			_lastResponseTime = DateTime.Now;
			Stream stream = await e.Response.GetContentAsync();
			if (stream == null)
			{
				return;
			}
			using StreamReader reader = new StreamReader(stream);
			string body = await reader.ReadToEndAsync();
			if (string.IsNullOrWhiteSpace(body) || body.Length < 10)
			{
				return;
			}
			_logger.Debug($"[WeixinLive] 响应体长度: {body.Length}");
			ParseApiResponse(body);
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			_logger.Error("[WeixinLive] 处理网络响应失败: " + ex2.Message, ex2);
		}
	}

	private void ParseApiResponse(string jsonContent)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(jsonContent);
			JsonElement rootElement = jsonDocument.RootElement;
			if (rootElement.TryGetProperty("errCode", out var value) && value.GetInt32() != 0)
			{
				_logger.Warn($"[WeixinLive] API 返回错误码: {value.GetInt32()}");
				return;
			}
			if (!rootElement.TryGetProperty("data", out var value2))
			{
				_logger.Debug("[WeixinLive] 响应中没有 data 字段");
				return;
			}
			if (value2.TryGetProperty("liveInfo", out var value3))
			{
				ProcessLiveInfo(value3);
			}
			if (value2.TryGetProperty("msgList", out var value4) && value4.ValueKind == JsonValueKind.Array)
			{
				int num = 0;
				foreach (JsonElement item in value4.EnumerateArray())
				{
					ProcessChatMessage(item);
					num++;
				}
				if (num > 0)
				{
					_logger.Debug($"[WeixinLive] 处理了 {num} 条弹幕消息");
				}
			}
			if (!value2.TryGetProperty("appMsgList", out var value5) || value5.ValueKind != JsonValueKind.Array)
			{
				return;
			}
			int num2 = 0;
			foreach (JsonElement item2 in value5.EnumerateArray())
			{
				ProcessAppMessage(item2);
				num2++;
			}
			if (num2 > 0)
			{
				_logger.Debug($"[WeixinLive] 处理了 {num2} 条应用消息");
			}
		}
		catch (Exception ex)
		{
			_logger.Error("[WeixinLive] 解析 API 响应失败: " + ex.Message, ex);
		}
	}

	private void ProcessLiveInfo(JsonElement liveInfo)
	{
		try
		{
			if (liveInfo.TryGetProperty("onlineCnt", out var value))
			{
				int @int = value.GetInt32();
				_messageAggregator.PublishMessage(new LiveMessage
				{
					Platform = "weixin_channels",
					MsgType = "viewer_count",
					Username = "系统",
					Content = $"当前观众: {@int}",
					ExtraData = new Dictionary<string, JsonElement>
					{
						["viewer_count"] = JsonSerializer.SerializeToElement(@int)
					},
					Timestamp = DateTime.Now
				});
				_logger.Debug($"[WeixinLive] 观众数: {@int}");
			}
			if (liveInfo.TryGetProperty("likeCnt", out var value2))
			{
				int int2 = value2.GetInt32();
				_messageAggregator.PublishMessage(new LiveMessage
				{
					Platform = "weixin_channels",
					MsgType = "like_count",
					Username = "系统",
					Content = int2.ToString(),
					Timestamp = DateTime.Now
				});
			}
		}
		catch (Exception ex)
		{
			_logger.Error("[WeixinLive] 处理直播信息失败: " + ex.Message, ex);
		}
	}

	private void ProcessChatMessage(JsonElement msg)
	{
		try
		{
			if (msg.TryGetProperty("type", out var value))
			{
				int @int = value.GetInt32();
				JsonElement value2;
				string text = (msg.TryGetProperty("nickname", out value2) ? (value2.GetString() ?? "未知用户") : "未知用户");
				JsonElement value3;
				string text2 = (msg.TryGetProperty("content", out value3) ? (value3.GetString() ?? "") : "");
				string text3;
				string text4;
				switch (@int)
				{
				case 1:
					text3 = "chat";
					text4 = text2;
					break;
				case 10005:
					text3 = "enter";
					text4 = "进入直播间";
					break;
				default:
					_logger.Debug($"[WeixinLive] 未知消息类型: {@int}");
					return;
				}
				_messageAggregator.PublishMessage(new LiveMessage
				{
					Platform = "weixin_channels",
					MsgType = text3,
					Username = text,
					Content = text4,
					Timestamp = DateTime.Now
				});
				_logger.Debug($"[WeixinLive] [{text3}] {text}: {text4}");
			}
		}
		catch (Exception ex)
		{
			_logger.Error("[WeixinLive] 处理聊天消息失败: " + ex.Message, ex);
		}
	}

	private void ProcessAppMessage(JsonElement appMsg)
	{
		try
		{
			if (appMsg.TryGetProperty("msgType", out var value))
			{
				int @int = value.GetInt32();
				string text = "未知用户";
				if (appMsg.TryGetProperty("fromUserContact", out var value2) && value2.TryGetProperty("contact", out var value3) && value3.TryGetProperty("nickname", out var value4))
				{
					text = value4.GetString() ?? "未知用户";
				}
				string text2;
				string text3;
				switch (@int)
				{
				case 20009:
					text2 = "gift";
					text3 = DecodePayloadContent(appMsg, "礼物");
					break;
				case 20006:
					text2 = "like";
					text3 = "点赞";
					break;
				case 20013:
					text2 = "gift";
					text3 = DecodePayloadContent(appMsg, "连击礼物");
					break;
				case 20031:
					text2 = "level_up";
					text3 = "等级提升";
					break;
				default:
					_logger.Debug($"[WeixinLive] 未知应用消息类型: {@int}");
					return;
				}
				_messageAggregator.PublishMessage(new LiveMessage
				{
					Platform = "weixin_channels",
					MsgType = text2,
					Username = text,
					Content = text3,
					Timestamp = DateTime.Now
				});
				_logger.Debug($"[WeixinLive] [{text2}] {text}: {text3}");
			}
		}
		catch (Exception ex)
		{
			_logger.Error("[WeixinLive] 处理应用消息失败: " + ex.Message, ex);
		}
	}

	private string DecodePayloadContent(JsonElement appMsg, string fallback)
	{
		try
		{
			if (appMsg.TryGetProperty("payload", out var value))
			{
				string? text = value.GetString();
				if (!string.IsNullOrEmpty(text))
				{
					string json = Encoding.UTF8.GetString(Convert.FromBase64String(text));
					using JsonDocument jsonDocument = JsonDocument.Parse(json);
					if (jsonDocument.RootElement.TryGetProperty("content", out var value2))
					{
						return value2.GetString() ?? fallback;
					}
				}
			}
		}
		catch (Exception ex)
		{
			_logger.Debug("[WeixinLive] 解码 payload 失败: " + ex.Message);
		}
		return fallback;
	}

	private async Task InjectHelperScriptAsync()
	{
		if (_coreWebView2 == null)
		{
			return;
		}
		try
		{
			string script = "\n                (function() {\n                    // 每45秒提取一次统计数据\n                    if (window.__weixinStatsTimer) clearInterval(window.__weixinStatsTimer);\n                    window.__weixinStatsTimer = setInterval(function() {\n                        let stats = { seen: 0, online: 0 };\n                        document.querySelectorAll('div, span, p').forEach(function(el) {\n                            let text = el.textContent || '';\n                            if ((text.includes('看过') || text.includes('累计观看')) && text.length < 30) {\n                                let m = text.match(/[\\d,.]+/);\n                                if (m) stats.seen = parseInt(m[0].replace(/,/g, ''));\n                            }\n                        });\n                        if (stats.seen > 0) {\n                            console.log('[WeixinStats] seen=' + stats.seen);\n                        }\n                    }, 45000);\n                    console.log('[WeixinHelper] 辅助脚本已注入');\n                })();\n            ";
			await _coreWebView2.ExecuteScriptAsync(script);
			_logger.Info("[WeixinLive] 辅助 JS 脚本已注入");
		}
		catch (Exception ex)
		{
			_logger.Debug("[WeixinLive] 注入辅助脚本失败: " + ex.Message);
		}
	}

	private void StartHealthChecks()
	{
		_healthCheckTimer = new Timer(delegate
		{
			if ((DateTime.Now - _lastResponseTime).TotalMinutes > 10.0)
			{
				_logger.Warn("[WeixinLive] 超过10分钟无网络活动，刷新页面...");
				Dispatcher.UIThread.Post(delegate
				{
					RefreshPage();
				});
			}
		}, null, TimeSpan.FromMinutes(1L), TimeSpan.FromMinutes(1L));
		_periodicRefreshTimer = new Timer(delegate
		{
			_logger.Info("[WeixinLive] 定期刷新页面以保持连接...");
			Dispatcher.UIThread.Post(delegate
			{
				RefreshPage();
			});
		}, null, TimeSpan.FromMinutes(50L), TimeSpan.FromMinutes(50L));
	}

	private void RefreshPage()
	{
		try
		{
			_coreWebView2?.Reload();
			_logger.Info("[WeixinLive] 页面已刷新");
			this.StatusChanged?.Invoke(this, "页面已刷新，继续监听...");
		}
		catch (Exception ex)
		{
			_logger.Error("[WeixinLive] 刷新页面失败: " + ex.Message, ex);
		}
	}

	public async Task StopAsync()
	{
		_logger.Info("[WeixinLive] 停止视频号服务...");
		_isMonitoring = false;
		try
		{
			_healthCheckTimer?.Dispose();
			_healthCheckTimer = null;
			_periodicRefreshTimer?.Dispose();
			_periodicRefreshTimer = null;
			_cancellationTokenSource?.Cancel();
			_cancellationTokenSource?.Dispose();
			_cancellationTokenSource = null;
			await Dispatcher.UIThread.InvokeAsync(delegate
			{
				if (_coreWebView2 != null)
				{
					_coreWebView2.WebResourceResponseReceived -= OnWebResourceResponseReceived;
					_coreWebView2.NavigationCompleted -= OnNavigationCompleted;
				}
				_webView?.Dispose();
				_webView = null;
				_coreWebView2 = null;
				_hostWindow?.Close();
				_hostWindow = null;
			});
			this.StatusChanged?.Invoke(this, "已停止");
			_logger.Info("[WeixinLive] 视频号服务已停止");
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			_logger.Error("[WeixinLive] 停止服务失败: " + ex2.Message, ex2);
		}
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			StopAsync().Wait();
		}
	}
}
