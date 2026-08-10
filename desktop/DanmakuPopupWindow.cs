using System;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using LiveDanmuDesktop.Services;

namespace LiveDanmuDesktop;

public sealed partial class DanmakuPopupWindow : Window
{
    private sealed class PopupSettings
    {
        public bool ShowChat { get; set; } = true;
        public bool ShowGift { get; set; } = true;
        public bool ShowLike { get; set; } = true;
        public bool ShowMember { get; set; } = true;
        public double BackgroundOpacity { get; set; } = 1;
    }

    private readonly ScrollViewer _scrollViewer;
    private readonly StackPanel _messagePanel;
    private readonly Slider _opacitySlider;
    private readonly TextBlock _opacityValue;
    private readonly Button _lockButton;
    private readonly Border _toolbar;
    private readonly Border _settingsPanel;
    private readonly SolidColorBrush _windowBackground = new(Color.Parse("#111315"));
    private readonly SolidColorBrush _toolbarBackground = new(Color.Parse("#171A1D"));
    private readonly SolidColorBrush _rowBackground = new(Color.Parse("#171A1D"));
    private readonly CheckBox _showChat = CreateFilter("弹幕", true);
    private readonly CheckBox _showGift = CreateFilter("礼物", true);
    private readonly CheckBox _showLike = CreateFilter("点赞", true);
    private readonly CheckBox _showMember = CreateFilter("进场", true);
    private bool _isLocked;
    private int _messageCount;
    private const int MaxMessages = 200;

    public DanmakuPopupWindow()
    {
        Title = "实时弹幕";
        Width = 460;
        Height = 680;
        MinWidth = 340;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
        ExtendClientAreaTitleBarHeightHint = -1;
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        CanResize = true;
        Topmost = true;

        _opacityValue = new TextBlock
        {
            Text = "100%", FontSize = 11, Foreground = Brush("#A0A6AA"),
            VerticalAlignment = VerticalAlignment.Center, Width = 34
        };
        _opacitySlider = new Slider
        {
            Minimum = 20, Maximum = 100, Value = 100, Width = 68,
            VerticalAlignment = VerticalAlignment.Center,
            [ToolTip.TipProperty] = "仅调整窗口背景透明度，弹幕文字始终保持清晰"
        };
        _opacitySlider.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty) return;
            double value = Math.Clamp(_opacitySlider.Value / 100, 0.2, 1);
            ApplyBackgroundOpacity(value);
            _opacityValue.Text = $"{Math.Round(value * 100)}%";
        };

        _lockButton = CreateToolButton("锁定", "锁定后禁止拖动和调整窗口大小");
        _lockButton.Click += (_, _) => ToggleLock();
        var settingsButton = CreateToolButton("设置", "选择独立弹幕窗需要显示的内容");
        settingsButton.Click += (_, _) =>
        {
            if (_settingsPanel is not null) _settingsPanel.IsVisible = !_settingsPanel.IsVisible;
        };
        var closeButton = CreateToolButton("关闭", "关闭独立弹幕窗口");
        closeButton.Click += (_, _) => Close();

        var opacityGroup = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
        opacityGroup.Children.Add(new TextBlock { Text = "背景", Foreground = Brush("#A0A6AA"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        opacityGroup.Children.Add(_opacitySlider);
        opacityGroup.Children.Add(_opacityValue);

        var toolbarContent = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto") };
        Grid.SetColumn(opacityGroup, 0);
        Grid.SetColumn(settingsButton, 1);
        Grid.SetColumn(_lockButton, 2);
        Grid.SetColumn(closeButton, 3);
        toolbarContent.Children.Add(opacityGroup);
        toolbarContent.Children.Add(settingsButton);
        toolbarContent.Children.Add(_lockButton);
        toolbarContent.Children.Add(closeButton);

        _toolbar = new Border
        {
            Background = _toolbarBackground, BorderBrush = Brush("#2A2F33"),
            BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(10, 8), Child = toolbarContent
        };
        _toolbar.PointerPressed += (_, e) =>
        {
            if (!_isLocked && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };

        var filters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        filters.Children.Add(_showChat);
        filters.Children.Add(_showGift);
        filters.Children.Add(_showLike);
        filters.Children.Add(_showMember);
        var settingsContent = new StackPanel { Spacing = 8 };
        settingsContent.Children.Add(new TextBlock { Text = "显示内容", FontSize = 12, FontWeight = FontWeight.DemiBold, Foreground = Brush("#F3F4F1") });
        settingsContent.Children.Add(filters);
        settingsContent.Children.Add(new TextBlock
        {
            Text = "提示：抖音使用红色标签，视频号使用绿色标签；透明度只影响背景。",
            FontSize = 11, Foreground = Brush("#929A9F"), TextWrapping = TextWrapping.Wrap
        });
        _settingsPanel = new Border
        {
            IsVisible = false, Background = _toolbarBackground, BorderBrush = Brush("#2A2F33"),
            BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(14, 12), Child = settingsContent
        };
        LoadSettings();
        foreach (var filter in new[] { _showChat, _showGift, _showLike, _showMember })
        {
            filter.PropertyChanged += (_, e) =>
            {
                if (e.Property == ToggleButton.IsCheckedProperty) SaveSettings();
            };
        }
        _opacitySlider.PointerReleased += (_, _) => SaveSettings();

        _messagePanel = new StackPanel { Spacing = 3, Margin = new Thickness(8) };
        _scrollViewer = new ScrollViewer
        {
            Content = _messagePanel, Background = Brushes.Transparent,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var root = new DockPanel { Background = _windowBackground };
        var resizeHint = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Background = _toolbarBackground };
        resizeHint.Children.Add(new TextBlock
        {
            Text = "拖动窗口边缘或右下角可调整大小", FontSize = 10, Foreground = Brush("#7F878C"),
            Margin = new Thickness(10, 4), VerticalAlignment = VerticalAlignment.Center
        });
        var resizeGrip = new TextBlock
        {
            Text = "◢", FontSize = 15, Foreground = Brush("#A0A6AA"), Cursor = new Cursor(StandardCursorType.BottomRightCorner),
            Padding = new Thickness(8, 3), [ToolTip.TipProperty] = "拖动调整弹幕窗口大小"
        };
        resizeGrip.PointerPressed += (_, e) =>
        {
            if (!_isLocked && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginResizeDrag(WindowEdge.SouthEast, e);
        };
        Grid.SetColumn(resizeGrip, 1);
        resizeHint.Children.Add(resizeGrip);
        DockPanel.SetDock(_toolbar, Dock.Top);
        DockPanel.SetDock(_settingsPanel, Dock.Top);
        DockPanel.SetDock(resizeHint, Dock.Bottom);
        root.Children.Add(_toolbar);
        root.Children.Add(_settingsPanel);
        root.Children.Add(resizeHint);
        root.Children.Add(_scrollViewer);
        Content = root;
    }

    private static SolidColorBrush Brush(string color) => new(Color.Parse(color));

    private void ApplyBackgroundOpacity(double opacity)
    {
        byte alpha = (byte)Math.Round(Math.Clamp(opacity, 0.2, 1) * 255);
        _windowBackground.Color = Color.FromArgb(alpha, 17, 19, 21);
        _toolbarBackground.Color = Color.FromArgb(alpha, 23, 26, 29);
        _rowBackground.Color = Color.FromArgb(alpha, 23, 26, 29);
        _windowBackground.Opacity = 1;
        _toolbarBackground.Opacity = 1;
        _rowBackground.Opacity = 1;
    }

    private static CheckBox CreateFilter(string text, bool isChecked) => new()
    {
        Content = text, IsChecked = isChecked, FontSize = 12, Foreground = Brush("#D7DADB"),
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Button CreateToolButton(string text, string tooltip) => new()
    {
        Content = text, MinWidth = 36, Height = 30, Margin = new Thickness(2, 0, 0, 0),
        FontSize = 12, Foreground = Brush("#C9CCCE"), Background = Brushes.Transparent,
        BorderThickness = new Thickness(0), Padding = new Thickness(4, 0),
        HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        Cursor = new Cursor(StandardCursorType.Hand), [ToolTip.TipProperty] = tooltip
    };

    private void ToggleLock()
    {
        _isLocked = !_isLocked;
        _lockButton.Content = _isLocked ? "解锁" : "锁定";
        CanResize = !_isLocked;
    }

    public void UpdateViewerCount(string platform, int count) { }

    public void AddDanmaku(string platform, string msgType, string user, string content)
    {
        Dispatcher.UIThread.Post(() =>
        {
            string normalizedType = NormalizeType(msgType);
            if (!ShouldShow(normalizedType)) return;
            bool isWeixin = platform.Contains("weixin", StringComparison.OrdinalIgnoreCase);
            string platformName = isWeixin ? "视频号" : "抖音";
            Color platformColor = Color.Parse(isWeixin ? "#44B978" : "#E45A4F");

            var messageText = new TextBlock
            {
                Text = $"{user}  {content}", Foreground = Brush("#FFFFFF"), FontSize = 13,
                TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center
            };
            var badge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(38, platformColor.R, platformColor.G, platformColor.B)),
                BorderBrush = new SolidColorBrush(platformColor), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2),
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = platformName, Foreground = new SolidColorBrush(platformColor), FontSize = 10, FontWeight = FontWeight.DemiBold }
            };
            var contentGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 9 };
            Grid.SetColumn(messageText, 1);
            contentGrid.Children.Add(badge);
            contentGrid.Children.Add(messageText);
            var row = new Border
            {
                Background = _rowBackground, BorderBrush = new SolidColorBrush(platformColor),
                BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(10, 8), Child = contentGrid
            };
            _messagePanel.Children.Add(row);
            _messageCount++;
            if (_messageCount > MaxMessages && _messagePanel.Children.Count > 0)
            {
                _messagePanel.Children.RemoveAt(0);
                _messageCount--;
            }
            Dispatcher.UIThread.Post(() => _scrollViewer.ScrollToEnd(), DispatcherPriority.Background);
        });
    }

    private bool ShouldShow(string type) => type switch
    {
        "gift" => _showGift.IsChecked == true,
        "like" => _showLike.IsChecked == true,
        "member" => _showMember.IsChecked == true,
        _ => _showChat.IsChecked == true
    };

    private static string NormalizeType(string type) => type.ToLowerInvariant() switch
    {
        "gift" => "gift",
        "like" => "like",
        "member" or "enter" or "social" or "system" => "member",
        _ => "chat"
    };

    private static string SettingsPath => AppPaths.GetDataPath("danmaku_popup_settings.json");

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var settings = JsonSerializer.Deserialize<PopupSettings>(File.ReadAllText(SettingsPath));
            if (settings is null) return;
            _showChat.IsChecked = settings.ShowChat;
            _showGift.IsChecked = settings.ShowGift;
            _showLike.IsChecked = settings.ShowLike;
            _showMember.IsChecked = settings.ShowMember;
            _opacitySlider.Value = Math.Clamp(settings.BackgroundOpacity, 0.2, 1) * 100;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[DanmakuPopup] 加载设置失败: " + ex.Message);
        }
    }

    private void SaveSettings()
    {
        try
        {
            var settings = new PopupSettings
            {
                ShowChat = _showChat.IsChecked == true,
                ShowGift = _showGift.IsChecked == true,
                ShowLike = _showLike.IsChecked == true,
                ShowMember = _showMember.IsChecked == true,
                BackgroundOpacity = _opacitySlider.Value / 100
            };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings));
        }
        catch (Exception ex)
        {
            Console.WriteLine("[DanmakuPopup] 保存设置失败: " + ex.Message);
        }
    }
}
