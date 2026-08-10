using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.XamlIl.Runtime;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using CompiledAvaloniaXaml;
using LiveDanmuDesktop.Services;
using Microsoft.Web.WebView2.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LiveDanmuDesktop;

public partial class WeixinLoginWindow : Window
{
	private class CookieValidationResult
	{
		public bool IsValid { get; set; }

		public string CookieString { get; set; } = string.Empty;

		public int CookieCount { get; set; }
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

	private WebView2? _webView;

	private TextBlock? _loadingText;

	private TextBlock? _statusText;

	private Button? _refreshButton;

	private Button? _saveButton;

	private Button? _closeButton;

	private readonly string[] _keyCookieNames = new string[8] { "uin", "sid", "skey", "wxuin", "pass_ticket", "sessionid", "sess_token", "openid" };

	private readonly string _configPath;

	public WeixinLoginWindow()
	{
		_configPath = AppPaths.GetDataPath("weixin_cookie_config.yaml");
		InitializeComponent();
		InitializeControls();
		InitializeWebView();
	}

	

	private void InitializeControls()
	{
		_loadingText = this.FindControl<TextBlock>("LoadingText");
		_statusText = this.FindControl<TextBlock>("StatusText");
		_refreshButton = this.FindControl<Button>("RefreshButton");
		_saveButton = this.FindControl<Button>("SaveButton");
		_closeButton = this.FindControl<Button>("CloseButton");
		if (_refreshButton != null)
		{
			_refreshButton.Click += RefreshButton_Click;
		}
		if (_saveButton != null)
		{
			_saveButton.Click += SaveButton_Click;
		}
		if (_closeButton != null)
		{
			_closeButton.Click += CloseButton_Click;
		}
	}

	private async void InitializeWebView()
	{
		try
		{
			_webView = new WebView2
			{
				HorizontalAlignment = HorizontalAlignment.Stretch,
				VerticalAlignment = VerticalAlignment.Stretch
			};
			if (base.Content is Grid mainGrid && mainGrid.RowDefinitions.Count > 1)
			{
				foreach (Control child in mainGrid.Children)
				{
					if (child is Border border && Grid.GetRow(border) == 1)
					{
						border.Child = new Grid
						{
							Children = { (Control)_webView }
						};
						break;
					}
				}
			}
			CoreWebView2Environment env = await AppPaths.CreateWebView2EnvironmentAsync("weixin-login");
			await _webView.EnsureCoreWebView2Async(env);
			if (_webView.CoreWebView2 != null)
			{
				CoreWebView2Settings settings = _webView.CoreWebView2.Settings;
				settings.IsScriptEnabled = true;
				settings.AreDefaultScriptDialogsEnabled = true;
				settings.IsWebMessageEnabled = true;
				_webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
				_webView.CoreWebView2.Navigate("https://channels.weixin.qq.com/login.html");
				if (_loadingText != null)
				{
					_loadingText.IsVisible = false;
				}
				if (_statusText != null)
				{
					_statusText.Text = "请使用微信扫码登录...";
				}
				Console.WriteLine("[WeixinLogin] WebView2 已初始化，导航到登录页面");
			}
			else if (_statusText != null)
			{
				_statusText.Text = "WebView2 初始化失败";
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			if (_statusText != null)
			{
				_statusText.Text = "错误: " + ex2.Message;
			}
			Console.WriteLine($"[WeixinLogin] 初始化失败: {ex2}");
		}
	}

	private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
	{
		if (!e.IsSuccess)
		{
			return;
		}
		string currentUrl = _webView?.CoreWebView2?.Source ?? "";
		Console.WriteLine("[WeixinLogin] 页面加载完成: " + currentUrl);
		Dispatcher.UIThread.Post(delegate
		{
			if (_statusText != null)
			{
				_statusText.Text = "页面已加载: " + currentUrl;
			}
		});
		if (!currentUrl.Contains("login.html") && currentUrl.Contains("channels.weixin.qq.com"))
		{
			Console.WriteLine("[WeixinLogin] 检测到登录成功，自动获取 Cookie...");
			await Task.Delay(2000);
			await AutoFetchAndSaveCookie();
		}
	}

	private async Task AutoFetchAndSaveCookie()
	{
		try
		{
			CookieValidationResult cookieResult = await FetchCookies();
			if (!cookieResult.IsValid)
			{
				return;
			}
			await SaveCookieToFile(cookieResult.CookieString);
			Dispatcher.UIThread.Post(delegate
			{
				if (_statusText != null)
				{
					_statusText.Text = $"Cookie 已自动保存（{cookieResult.CookieCount} 个），窗口即将最小化";
				}
			});
			await Task.Delay(900);
			Dispatcher.UIThread.Post(() => WindowState = WindowState.Minimized);
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Console.WriteLine($"[WeixinLogin] 自动保存 Cookie 失败: {ex2}");
		}
	}

	private async Task<CookieValidationResult> FetchCookies()
	{
		try
		{
			if (_webView?.CoreWebView2 == null)
			{
				return new CookieValidationResult
				{
					IsValid = false
				};
			}
			CoreWebView2CookieManager cookieManager = _webView.CoreWebView2.CookieManager;
			string[] domains = new string[4] { "https://channels.weixin.qq.com", "https://weixin.qq.com", "https://qq.com", "https://res.wx.qq.com" };
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
			bool hasValidContent = !string.IsNullOrWhiteSpace(cookieString) && cookieString.Length > 50;
			bool hasKeyCookie = _keyCookieNames.Any((string keyName) => uniqueCookies.Any((CoreWebView2Cookie c) => c.Name.Equals(keyName, StringComparison.OrdinalIgnoreCase)));
			bool isValid = hasValidContent && (hasKeyCookie || uniqueCookies.Count > 5);
			Console.WriteLine($"[WeixinLogin] 获取到 {uniqueCookies.Count} 个 Cookie, hasKeyCookie={hasKeyCookie}, isValid={isValid}");
			return new CookieValidationResult
			{
				IsValid = isValid,
				CookieString = cookieString,
				CookieCount = uniqueCookies.Count
			};
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Console.WriteLine($"[WeixinLogin] 获取 Cookie 失败: {ex2}");
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
			WeixinCookieConfig config = new WeixinCookieConfig
			{
				Cookie = new WeixinCookieData
				{
					Weixin = cookieString
				},
				LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
			};
			ISerializer serializer = new SerializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).Build();
			string yaml = serializer.Serialize(config);
			await File.WriteAllTextAsync(_configPath, yaml, Encoding.UTF8);
			Console.WriteLine("[WeixinLogin] Cookie 已保存到: " + _configPath);
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
					string rootPath = Path.Combine(searchDir.FullName, "weixin_cookie_config.yaml");
					try
					{
						await File.WriteAllTextAsync(rootPath, yaml, Encoding.UTF8);
						Console.WriteLine("[WeixinLogin] Cookie 同步到项目根目录: " + rootPath);
					}
					catch (Exception ex)
					{
						Exception copyEx = ex;
						Console.WriteLine("[WeixinLogin] 同步到项目根目录失败: " + copyEx.Message);
					}
					break;
				}
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Console.WriteLine($"[WeixinLogin] 保存 Cookie 失败: {ex2}");
			throw;
		}
	}

	private void RefreshButton_Click(object? sender, RoutedEventArgs e)
	{
		try
		{
			if (_webView?.CoreWebView2 != null)
			{
				_webView.CoreWebView2.Reload();
				if (_statusText != null)
				{
					_statusText.Text = "正在刷新页面...";
				}
			}
		}
		catch (Exception ex)
		{
			if (_statusText != null)
			{
				_statusText.Text = "刷新失败: " + ex.Message;
			}
		}
	}

	private async void SaveButton_Click(object? sender, RoutedEventArgs e)
	{
		try
		{
			if (_statusText != null)
			{
				_statusText.Text = "正在手动保存 Cookie...";
			}
			CookieValidationResult cookieResult = await FetchCookies();
			if (cookieResult.IsValid)
			{
				await SaveCookieToFile(cookieResult.CookieString);
				if (_statusText != null)
				{
					_statusText.Text = $"✓ Cookie 已保存 ({cookieResult.CookieCount} 个)";
				}
			}
			else if (_statusText != null)
			{
				_statusText.Text = "❌ 未检测到有效 Cookie，请先完成登录";
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			if (_statusText != null)
			{
				_statusText.Text = "❌ 保存失败: " + ex2.Message;
			}
			Console.WriteLine($"[WeixinLogin] 保存Cookie失败: {ex2}");
		}
	}

	private void CloseButton_Click(object? sender, RoutedEventArgs e)
	{
		Close();
	}

	protected override void OnClosed(EventArgs e)
	{
		base.OnClosed(e);
		if (_webView?.CoreWebView2 != null)
		{
			_webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
		}
		if (_webView != null)
		{
			_webView.Dispose();
			_webView = null;
		}
	}

	

	
}
