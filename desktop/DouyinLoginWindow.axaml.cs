using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
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

namespace LiveDanmuDesktop;

public partial class DouyinLoginWindow : Window
{
	private WebView2? _webView;

	private TextBlock? _loadingText;

	private TextBlock? _statusText;

	private Button? _refreshButton;

	private Button? _saveButton;

	private Button? _closeButton;

	private DouyinCookieAutoFetcher? _cookieFetcher;

	public DouyinLoginWindow()
	{
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
			CoreWebView2Environment env = await AppPaths.CreateWebView2EnvironmentAsync("douyin-login");
			await _webView.EnsureCoreWebView2Async(env);
			if (_webView.CoreWebView2 != null)
			{
				InitializeCookieFetcher();
				_webView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = true;
				_webView.CoreWebView2.Settings.IsGeneralAutofillEnabled = true;
				_webView.CoreWebView2.Navigate("https://www.douyin.com/");
				if (_loadingText != null)
				{
					_loadingText.IsVisible = false;
				}
				if (_statusText != null)
				{
					_statusText.Text = "请在浏览器中登录抖音...";
				}
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
			Console.WriteLine($"[DouyinLogin] 初始化失败: {ex2}");
		}
	}

	private void InitializeCookieFetcher()
	{
		if (_webView?.CoreWebView2 == null)
		{
			return;
		}
		try
		{
			_cookieFetcher = new DouyinCookieAutoFetcher(_webView.CoreWebView2, AppPaths.GetDataPath("cookie_config.yaml"), 30, 3000);
			_cookieFetcher.StatusUpdated += OnCookieStatusUpdated;
			_cookieFetcher.CookieFetched += OnCookieFetched;
			_cookieFetcher.CookieError += OnCookieError;
			_cookieFetcher.StartAutoMonitoring();
			Console.WriteLine("[DouyinLogin] Cookie 自动获取器已启动");
		}
		catch (Exception value)
		{
			Console.WriteLine($"[DouyinLogin] Cookie 获取器初始化失败: {value}");
		}
	}

	private void OnCookieStatusUpdated(object? sender, string status)
	{
		Dispatcher.UIThread.Post(delegate
		{
			if (_statusText != null)
			{
				_statusText.Text = status;
			}
		});
	}

	private void OnCookieFetched(object? sender, CookieFetchedEventArgs e)
	{
		Dispatcher.UIThread.Post(delegate
		{
			if (_statusText != null)
			{
				_statusText.Text = $"Cookie 已自动保存（{e.CookieCount} 个），可以关闭窗口";
			}
			if (_saveButton != null)
			{
				_saveButton.IsEnabled = true;
			}
			Console.WriteLine($"[DouyinLogin] Cookie 获取成功，共 {e.CookieCount} 个");
		});
	}

	private void OnCookieError(object? sender, CookieErrorEventArgs e)
	{
		Dispatcher.UIThread.Post(delegate
		{
			if (_statusText != null)
			{
				_statusText.Text = "❌ " + e.ErrorMessage;
			}
		});
	}

	private async void RefreshButton_Click(object? sender, RoutedEventArgs e)
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
			Exception ex2 = ex;
			if (_statusText != null)
			{
				_statusText.Text = "刷新失败: " + ex2.Message;
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
			if (_cookieFetcher != null)
			{
				if (!(await _cookieFetcher.ManualFetchCookie()) && _statusText != null)
				{
					_statusText.Text = "❌ 未检测到有效 Cookie，请先登录";
				}
			}
			else if (_statusText != null)
			{
				_statusText.Text = "❌ Cookie 获取器未初始化";
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			if (_statusText != null)
			{
				_statusText.Text = "❌ 保存失败: " + ex2.Message;
			}
			Console.WriteLine($"[DouyinLogin] 保存Cookie失败: {ex2}");
		}
	}

	private void CloseButton_Click(object? sender, RoutedEventArgs e)
	{
		Close();
	}

	protected override void OnClosed(EventArgs e)
	{
		_cookieFetcher?.StopAutoMonitoring();
		if (_cookieFetcher != null)
		{
			_cookieFetcher.StatusUpdated -= OnCookieStatusUpdated;
			_cookieFetcher.CookieFetched -= OnCookieFetched;
			_cookieFetcher.CookieError -= OnCookieError;
		}
		if (_webView != null)
		{
			_webView.Dispose();
			_webView = null;
		}
		base.OnClosed(e);
	}

	

	
}
