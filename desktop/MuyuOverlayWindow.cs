using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using LiveDanmuDesktop.Services;
using Microsoft.Web.WebView2.Core;

namespace LiveDanmuDesktop;

public partial class MuyuOverlayWindow : Window
{
	private WebView2? _webView;

	private bool _webViewNavigated = false;

	private bool _isLocked;

	private readonly Border _toolbar;

	private readonly Button _lockBtn;

	private readonly string _platform;

	public WebView2? OverlayWebView => _webView;

	public event EventHandler? WebViewReady;

	public void PostMessage(string json)
	{
		if (_webView == null || !_webViewNavigated)
		{
			return;
		}
		Dispatcher.UIThread.Post(async delegate
		{
			try
			{
				if (_webView?.CoreWebView2 == null || !_webViewNavigated)
				{
					Console.WriteLine("[MuyuOverlay] ❌ PostMessage 跳过：WebView 未就绪");
				}
				else
				{
					string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
					string script = "\r\n                    try {\r\n                        if(window.onHostMessage) {\r\n                            onHostMessage(JSON.parse(atob('" + b64 + "')));\r\n                            'ok';\r\n                        } else {\r\n                            'no-handler';\r\n                        }\r\n                    } catch(e) {\r\n                        'error:' + e.message;\r\n                    }";
					string result = await _webView.CoreWebView2.ExecuteScriptAsync(script);
					if (result != null && result != "\"ok\"")
					{
						Console.WriteLine("[MuyuOverlay] ⚠\ufe0f PostMessage result: " + result);
					}
				}
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				Console.Error.WriteLine("[MuyuOverlay] PostMessage failed: " + ex2.Message);
			}
		});
	}

	public MuyuOverlayWindow()
		: this("weixin")
	{
	}

	public MuyuOverlayWindow(string platform)
	{
		_platform = platform;
		base.Title = "木鱼 - " + ((platform == "weixin") ? "视频号" : "抖音") + " (透明)";
		base.Width = 350.0;
		base.Height = 420.0;
		base.MinWidth = 200.0;
		base.MinHeight = 250.0;
		base.WindowStartupLocation = WindowStartupLocation.CenterScreen;
		base.ExtendClientAreaToDecorationsHint = true;
		base.ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
		base.ExtendClientAreaTitleBarHeightHint = -1.0;
		base.SystemDecorations = SystemDecorations.None;
		base.TransparencyLevelHint = new WindowTransparencyLevel[1] { WindowTransparencyLevel.Transparent };
		base.Background = Brushes.Transparent;
		base.CanResize = true;
		base.Topmost = true;
		_toolbar = new Border
		{
			Background = new SolidColorBrush(Avalonia.Media.Color.Parse("#000000"), 0.4),
			Padding = new Thickness(6.0, 3.0),
			CornerRadius = new CornerRadius(6.0),
			HorizontalAlignment = HorizontalAlignment.Center,
			Margin = new Thickness(0.0, 4.0, 0.0, 0.0)
		};
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 6.0,
			HorizontalAlignment = HorizontalAlignment.Center
		};
		TextBlock item = new TextBlock
		{
			Text = "⠿",
			Foreground = new SolidColorBrush(Avalonia.Media.Color.Parse("#aaaaaa")),
			FontSize = 14.0,
			VerticalAlignment = VerticalAlignment.Center,
			Cursor = new Cursor(StandardCursorType.SizeAll)
		};
		_lockBtn = CreateToolButton("\ud83d\udd13", "锁定/解锁");
		_lockBtn.Click += delegate
		{
			ToggleLock();
		};
		Button button = CreateToolButton("✕", "关闭");
		button.Click += delegate
		{
			Close();
		};
		stackPanel.Children.Add(item);
		stackPanel.Children.Add(_lockBtn);
		stackPanel.Children.Add(button);
		_toolbar.Child = stackPanel;
		_toolbar.PointerPressed += delegate(object? _, PointerPressedEventArgs e)
		{
			if (!_isLocked && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
			{
				BeginMoveDrag(e);
			}
		};
		DockPanel dockPanel = new DockPanel
		{
			Background = Brushes.Transparent
		};
		DockPanel.SetDock(_toolbar, Dock.Top);
		dockPanel.Children.Add(_toolbar);
		Panel webViewHost = new Panel
		{
			Background = Brushes.Transparent,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Stretch
		};
		dockPanel.Children.Add(webViewHost);
		base.Content = dockPanel;
		base.Opened += async delegate
		{
			await InitWebView(webViewHost);
		};
	}

	private async Task InitWebView(Panel host)
	{
		try
		{
			_webView = new WebView2
			{
				HorizontalAlignment = HorizontalAlignment.Stretch,
				VerticalAlignment = VerticalAlignment.Stretch
			};
			host.Children.Add(_webView);
			CoreWebView2Environment env = await AppPaths.CreateWebView2EnvironmentAsync();
			await _webView.EnsureCoreWebView2Async(env);
			if (_webView.CoreWebView2 == null)
			{
				return;
			}
			this.WebViewReady?.Invoke(this, EventArgs.Empty);
			_webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
			_webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
			string basePath = AppPaths.RuntimeRoot;
			string htmlPath = Path.Combine(basePath, "wwwroot", "muyu-display.html");
			if (File.Exists(htmlPath))
			{
				string uri = "file:///" + htmlPath.Replace('\\', '/') + "?platform=" + _platform;
				_webView.CoreWebView2.NavigationCompleted += delegate
				{
					_webViewNavigated = true;
					Console.WriteLine("[MuyuOverlay] 页面加载完成，可接收数据");
				};
				_webView.CoreWebView2.Navigate(uri);
			}
			else
			{
				Console.Error.WriteLine("[MuyuOverlay] 文件不存在: " + htmlPath);
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Console.Error.WriteLine("[MuyuOverlay] WebView2 初始化失败: " + ex2.Message);
		}
	}

	private Button CreateToolButton(string text, string tooltip)
	{
		return new Button
		{
			Content = text,
			Width = 24.0,
			Height = 24.0,
			FontSize = 12.0,
			Foreground = new SolidColorBrush(Avalonia.Media.Color.Parse("#cccccc")),
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0.0),
			Padding = new Thickness(0.0),
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
			Cursor = new Cursor(StandardCursorType.Hand),
			[ToolTip.TipProperty] = tooltip
		};
	}

	private void ToggleLock()
	{
		_isLocked = !_isLocked;
		_lockBtn.Content = (_isLocked ? "\ud83d\udd12" : "\ud83d\udd13");
		base.CanResize = !_isLocked;
		_toolbar.Opacity = (_isLocked ? 0.3 : 1.0);
	}

	protected override void OnClosed(EventArgs e)
	{
		_webView?.Dispose();
		_webView = null;
		base.OnClosed(e);
	}
}
