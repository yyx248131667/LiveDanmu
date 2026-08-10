using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using LiveDanmuDesktop.Models;
using LiveDanmuDesktop.Services;

namespace LiveDanmuDesktop;

/// <summary>
/// 主窗口：WebView 宿主，前后端消息桥梁
/// </summary>
public partial class MainWindow : Window
{
    private Avalonia.Controls.WebView2? _webView;
    private bool _webViewReady;
    private DanmakuPopupWindow? _popupWindow;
    private DouyinLoginWindow? _douyinLoginWindow;
    private WeixinLoginWindow? _weixinLoginWindow;
    private readonly Dictionary<string, MuyuOverlayWindow> _muyuOverlayWindows = new();

    private MuyuConfigService _muyuConfigService = null!;
    private MuyuService _muyuService = null!;
    private LiveServiceManager? _liveServiceManager;
    private Logger _logger = null!;
    
    // 多直播间监控（弹幕保存模块）
    private readonly System.Collections.Generic.List<DouyinDirectService> _extraDouyinMonitors = new();
    // 弹幕保存专用的视频号嗅探服务，独立于主服务和木鱼模块，使用代理脚本注入方式。
    private WeixinSnifferService? _danmuWeixinSniffer;
    private readonly System.Collections.Generic.List<Avalonia.Controls.WebView2> _popupWebViews = new();
    
    // 幸运九宫格抽奖
    private bool _lotteryMode = false;
    private Avalonia.Controls.WebView2? _lotteryOverlayWebView;
    private string _lotteryTriggerGift = ""; // 触发礼物名称
    
    // 像素问号弹幕抽奖
    private bool _mysteryMode = false;
    private Avalonia.Controls.WebView2? _mysteryOverlayWebView;
    private string _mysteryKeyword = "抽奖";
    private string _mysteryPlatform = "douyin";
    
    

    // douyinLive 外部进程 (https://github.com/jwwsjlm/douyinLive)
    private Process? _douyinLiveProcess;

    public MainWindow()
    {
        InitializeComponent();
        InitializeServices();
        InitializeWebView();
    }

    private async void InitializeWebView()
    {
        try
        {
            _webView = new Avalonia.Controls.WebView2();

            var panel = this.FindControl<Panel>("rootPanel")!;
            panel.Children.Add(_webView);

            // 等待 CoreWebView2 初始化，并使用稳定的 UserDataFolder。
            var env = await Services.AppPaths.CreateWebView2EnvironmentAsync();
            await _webView.EnsureCoreWebView2Async(env);
            Console.WriteLine("[MainWindow] CoreWebView2 initialized");

            var coreWebView = _webView.CoreWebView2
                ?? throw new InvalidOperationException("WebView2 核心初始化失败");

            // 向所有 frame（包括 iframe）注入脚本；视频号控制台中隔离视频画面。
            await coreWebView.AddScriptToExecuteOnDocumentCreatedAsync(@"
                if (window.location.hostname === 'channels.weixin.qq.com') {
                    setInterval(() => {
                        let video = document.querySelector('video');
                        if (video && !video.dataset.isolated) {
                            video.dataset.isolated = 'true';
                            video.style.position = 'fixed';
                            video.style.top = '0';
                            video.style.left = '0';
                            video.style.width = '100vw';
                            video.style.height = '100vh';
                            video.style.zIndex = '999999';
                            video.style.objectFit = 'contain';
                            video.style.background = 'black';
                            document.body.style.overflow = 'hidden';
                        }
                    }, 1000);
                }
            ");

            // JavaScript 与 C# 通信
            _webView.WebMessageReceived += (_, args) =>
            {
                var json = args.WebMessageAsJson;
                string message;
                try { message = JsonSerializer.Deserialize<string>(json) ?? json; }
                catch { message = json; }
                Dispatcher.UIThread.Post(() => OnWebMessageReceived(message));
            };


            // 导航完成后推送初始配置
            _webView.NavigationCompleted += (_, _) =>
            {
                Console.WriteLine("[MainWindow] NavigationCompleted");
                Dispatcher.UIThread.Post(async () =>
                {
                    await Task.Delay(300);
                    _webViewReady = true;
                    OnWebViewReady();
                });
            };

            var htmlPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
            Console.WriteLine($"[MainWindow] HTML path: {htmlPath}");

            if (File.Exists(htmlPath))
            {
                var uri = new Uri("file:///" + htmlPath.Replace('\\', '/'));
                _webView.Source = uri;
                Console.WriteLine($"[MainWindow] Loading: {uri}");
            }
            else
            {
                Console.Error.WriteLine("[MainWindow] index.html not found");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MainWindow] WebView init failed: {ex}");
            ShowFallbackContent($"WebView 初始化失败：{ex.Message}");
        }
    }

    private void InitializeServices()
    {
        _logger = new Logger();
        _muyuConfigService = new MuyuConfigService();
        _muyuService = new MuyuService(_muyuConfigService, _logger);
        
        // 初始化直播服务管理器
        _liveServiceManager = new LiveServiceManager(_logger);
        _liveServiceManager.MessageReceived += OnLiveMessageReceived;
        _liveServiceManager.StatusChanged += OnServiceStatusChanged;
        
        SubscribeMuyuEvents();
    }

    private void ShowFallbackContent(string message)
    {
        var panel = this.FindControl<Panel>("rootPanel")!;
        var textBlock = new TextBlock
        {
            Text = message,
            Foreground = Avalonia.Media.Brushes.White,
            FontSize = 16,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        panel.Children.Add(textBlock);
    }

    private void SubscribeMuyuEvents()
    {
        _muyuService.MuyuHit += (_, e) =>
        {
            var msg = new
            {
                type = "muyu-hit",
                platform = e.Platform,
                method = e.Method,
                hits = e.Hits, text = e.Text, user = e.User,
                logContent = e.LogContent, totalCount = e.TotalCount,
                likeCount = e.LikeCount, giftCount = e.GiftCount,
                playSound = e.PlaySound, volume = e.Volume,
                audioSpeed = e.AudioSpeed, likeRate = e.LikeRate, giftRate = e.GiftRate
            };
            var json = JsonSerializer.Serialize(msg, MuyuConfig.JsonOptions);

            Console.WriteLine($"[MuyuHit→UI] hits={e.Hits}, total={e.TotalCount}, webViewReady={_webViewReady}, overlayCount={_muyuOverlayWindows.Count}, popupWvCount={_popupWebViews.Count}");

            PostMessageToWeb(json);
            // PostMessageToWeb 已向 _popupWebViews（包括 overlay）推送，无需重复发送。
        };

        _muyuService.ConfigChanged += (_, e) =>
        {
            var msg = new { type = "muyu-config", platform = e.Platform, config = e.Config };
            PostMessageToWeb(JsonSerializer.Serialize(msg, MuyuConfig.JsonOptions));
        };

        _muyuService.CounterReset += (_, _) =>
        {
            PostMessageToWeb(JsonSerializer.Serialize(new { type = "muyu-reset" }, MuyuConfig.JsonOptions));
        };

        _muyuService.GiftCollected += (_, e) =>
        {
            var msg = new
            {
                type = "gift-collected", platform = e.Platform,
                giftId = e.GiftId, giftName = e.GiftName, totalGifts = e.TotalGifts
            };
            PostMessageToWeb(JsonSerializer.Serialize(msg, MuyuConfig.JsonOptions));
        };

        _muyuService.DanmakuReceived += (_, e) =>
        {
            var msg = new
            {
                type = "danmaku", msgType = e.MsgType,
                platform = e.Platform, user = e.User, content = e.Content
            };
            PostMessageToWeb(JsonSerializer.Serialize(msg, MuyuConfig.JsonOptions));
            _popupWindow?.AddDanmaku(e.Platform, e.MsgType, e.User, e.Content);
        };

        _muyuService.ViewerCountUpdated += (_, e) =>
        {
            var msg = new { type = "viewer-count", platform = e.Platform, count = e.Count };
            PostMessageToWeb(JsonSerializer.Serialize(msg, MuyuConfig.JsonOptions));
            _popupWindow?.UpdateViewerCount(e.Platform, e.Count);
        };
    }

    public void OnLiveMessageReceived(object? sender, LiveMessage message)
    {
        Console.WriteLine($"[MainWindow] OnLiveMessageReceived: MsgType={message.MsgType}, Method={message.Method}, Platform={message.Platform}, User={message.Username}");
        
        // 像素问号模式：弹幕消息同时转发到 overlay 进行抽奖匹配。
        if (_mysteryMode && string.Equals(message.Platform, _mysteryPlatform, StringComparison.OrdinalIgnoreCase))
        {
            if (message.Method == "WebcastChatMessage" || message.MsgType == "danmaku" || message.MsgType == "chat")
            {
                HandleMysteryDanmakuMessage(message);
            }
            // 消息继续交给 MuyuService，以便显示在弹幕列表中。
            // SuppressMuyuHit=true 时仅阻止木鱼敲击。
        }



        // 九宫格模式：将礼物消息转发到抽奖 overlay。
        if (_lotteryMode && string.Equals(message.Platform, "douyin", StringComparison.OrdinalIgnoreCase) && 
            (message.Method == "WebcastGiftMessage" || message.MsgType == "gift"))
        {
            HandleLotteryGiftMessage(message);
            return; // 九宫格模式下不转发给木鱼
        }
        

        
        _muyuService.ProcessMessage(message);
    }

    public void OnWebViewReady()
    {
        Console.WriteLine("[MainWindow] WebView ready, pushing initial config...");
        var platform = _muyuService.ActivePlatform;
        var config = _muyuService.GetConfig(platform);
        var configMsg = new { type = "muyu-config", platform, config };
        PostMessageToWeb(JsonSerializer.Serialize(configMsg, MuyuConfig.JsonOptions));

        PostMessageToWeb(JsonSerializer.Serialize(
            new { type = "ws-status", platform = "douyin", status = "disconnected", text = "未连" },
            MuyuConfig.JsonOptions));
        PostMessageToWeb(JsonSerializer.Serialize(
            new { type = "ws-status", platform = "weixin", status = "disconnected", text = "未连" },
            MuyuConfig.JsonOptions));

        // WebView 就绪后自动启动后端。
        AutoStartBackend();
    }

    public void OnWebMessageReceived(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeProp)) return;
            var type = typeProp.GetString();

            switch (type)
            {
                case "connect": HandleConnect(root); break;
                case "disconnect": HandleDisconnect(); break;
                case "updateSettings": HandleUpdateSettings(root); break;
                case "save-config": HandleSaveConfig(root); break;
                case "switch-platform": HandleSwitchPlatform(root); break;
                case "reset-muyu": _muyuService.ResetCounters(); break;
                case "upload-skin": HandleUploadSkin(root); break;
                case "popDanmaku": HandlePopDanmaku(); break;
                case "openMuyuOverlay": HandleOpenMuyuOverlay(root); break;
                case "openWeixinWebView": HandleOpenWeixinWebView(); break;
                case "openWeixinLogin": HandleOpenWeixinWebView(); break;
                case "openDouyinLogin": HandleOpenDouyinLogin(); break;
                case "refreshLiveRooms": HandleRefreshLiveRooms(); break;
                // 弹幕保存功能：使用 douyinLive 开源项目。
                case "openDanmuSave": HandleOpenDanmuSave(); break;
                case "startDouyinLiveExe": StartDouyinLiveExe(); break;
                case "startWeixinSniffer": HandleStartWeixinSniffer(); break;
                case "stopWeixinSniffer": HandleStopWeixinSniffer(); break;
                // 九宫格抽奖
                case "startLotteryMode": HandleStartLotteryMode(); break;
                case "stopLotteryMode": HandleStopLotteryMode(); break;
                case "openLotteryOverlay": HandleOpenLotteryOverlay(root); break;
                case "lotteryManualSpin": HandleLotteryManualSpin(); break;
                case "lotteryConfigUpdate": HandleLotteryConfigUpdate(root); break;
                // 像素问号弹幕抽奖
                case "startMysteryMode": HandleStartMysteryMode(root); break;
                case "stopMysteryMode": HandleStopMysteryMode(); break;
                case "openMysteryOverlay": HandleOpenMysteryOverlay(root); break;
                case "mysteryManualDraw": HandleMysteryManualDraw(); break;
                case "mysteryConfigUpdate": HandleMysteryConfigUpdate(root); break;
                case "mysteryClearPool": HandleMysteryClearPool(); break;

                case "close": Close(); break;
                case "minimize": WindowState = WindowState.Minimized; break;
                case "maximize":
                    WindowState = WindowState == WindowState.Maximized
                        ? WindowState.Normal : WindowState.Maximized;
                    break;
                case "opacity":
                    if (root.TryGetProperty("value", out var opacityVal) &&
                        opacityVal.TryGetDouble(out var opacity))
                        Opacity = Math.Clamp(opacity, 0.1, 1.0);
                    break;
                case "drag-move":
                    HandleDragMove(root);
                    break;
            }
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"[MainWindow] Failed to parse message: {ex.Message}");
        }
    }

    private async void HandleConnect(JsonElement root)
    {
        var douyinUrl = root.TryGetProperty("douyinUrl", out var du) ? du.GetString() : null;
        var weixinUrl = root.TryGetProperty("weixinUrl", out var wu) ? wu.GetString() : null;
        Console.WriteLine($"[MainWindow] Connect requested - douyin: {douyinUrl}, weixin: {weixinUrl}");

        try
        {
            // 从URL中提取房间ID
            string? douyinRoomId = null;
            string? weixinRoomId = null;

            if (!string.IsNullOrEmpty(douyinUrl))
            {
                // 从抖音 URL 提取房间 ID，例如 https://live.douyin.com/975816634199。
                var match = System.Text.RegularExpressions.Regex.Match(douyinUrl, @"live\.douyin\.com/(\d+)");
                if (match.Success)
                {
                    douyinRoomId = match.Groups[1].Value;
                }
            }

            if (!string.IsNullOrWhiteSpace(douyinUrl) && string.IsNullOrWhiteSpace(douyinRoomId))
            {
                PostMessageToWeb(JsonSerializer.Serialize(new
                {
                    type = "error",
                    message = "无法识别抖音直播间地址，请使用 https://live.douyin.com/数字房间号。"
                }, MuyuConfig.JsonOptions));
                return;
            }

            if (!string.IsNullOrEmpty(weixinUrl))
            {
                // 视频号使用完整URL
                weixinRoomId = weixinUrl;
            }

            // 创建配置并启动服务。
            var config = new LiveConfig
            {
                EnableDouyin = !string.IsNullOrEmpty(douyinRoomId),
                DouyinRoomId = douyinRoomId ?? "",
                EnableWeixin = !string.IsNullOrEmpty(weixinRoomId),
                WeixinRoomId = weixinRoomId ?? "",
                WeixinHeadless = false
            };

            if (_liveServiceManager != null)
            {
                var currentStatus = _liveServiceManager.GetStatus();
                if (currentStatus.douyinRunning || currentStatus.weixinRunning)
                {
                    await _liveServiceManager.StopAsync();
                }
                await _liveServiceManager.StartAsync(config);
                Console.WriteLine("[MainWindow] 后端服务已启");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MainWindow] 启动后端服务失败: {ex.Message}");
            PostMessageToWeb(JsonSerializer.Serialize(new
            {
                type = "error",
                message = $"启动失败: {ex.Message}"
            }, MuyuConfig.JsonOptions));
        }
    }

    private async void HandleDisconnect()
    {
        Console.WriteLine("[MainWindow] Disconnect requested");
        
        try
        {
            if (_liveServiceManager != null)
            {
                await _liveServiceManager.StopAsync();
                Console.WriteLine("[MainWindow] 后端服务已停");
            }
            PostMessageToWeb(JsonSerializer.Serialize(new
            {
                type = "ws-status",
                platform = "douyin",
                status = "disconnected",
                text = "已断开"
            }, MuyuConfig.JsonOptions));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MainWindow] 停止后端服务失败: {ex.Message}");
            PostMessageToWeb(JsonSerializer.Serialize(new
            {
                type = "error",
                message = $"断开连接失败: {ex.Message}"
            }, MuyuConfig.JsonOptions));
        }
    }

    /// <summary>
    /// 刷新直播间：停止后重新建立所有连接。
    /// </summary>
    private async void HandleRefreshLiveRooms()
    {
        Console.WriteLine("[MainWindow] 正在刷新直播间...");
        try
        {
            if (_liveServiceManager != null)
            {
                await _liveServiceManager.StopAsync();
                Console.WriteLine("[MainWindow] 服务已停止，重新启动...");
                
                // 重新加载配置
                var configPath = Path.Combine(Services.AppPaths.RuntimeRoot, "live_config.json");
                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    using var doc = JsonDocument.Parse(json);
                    var r = doc.RootElement;
                    var douyinRoomId = r.TryGetProperty("douyin_room_id", out var d) ? d.GetString() : null;
                    var weixinRoomId = r.TryGetProperty("weixin_room_id", out var w) ? w.GetString() : null;
                    var enableDouyin = r.TryGetProperty("enable_douyin", out var ed) && ed.GetBoolean();
                    var enableWeixin = r.TryGetProperty("enable_weixin", out var ew) && ew.GetBoolean();

                    var config = new LiveConfig
                    {
                        EnableDouyin = enableDouyin && !string.IsNullOrEmpty(douyinRoomId),
                        DouyinRoomId = douyinRoomId ?? "",
                        EnableWeixin = enableWeixin && !string.IsNullOrEmpty(weixinRoomId),
                        WeixinRoomId = weixinRoomId ?? "",
                        WeixinHeadless = false
                    };

                    await _liveServiceManager.StartAsync(config);
                    Console.WriteLine("[MainWindow]  直播间刷新完");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MainWindow] 刷新直播间失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 处理设置更新并重新连接。
    /// </summary>
    private async void HandleUpdateSettings(JsonElement root)
    {
        try
        {
            Console.WriteLine("========================================");
            Console.WriteLine("[MainWindow] 收到设置更新请求");

            if (!root.TryGetProperty("settings", out var settingsElement))
            {
                Console.WriteLine("[MainWindow] 设置数据缺失");
                return;
            }

            var douyinUrl = settingsElement.TryGetProperty("douyinUrl", out var du) ? du.GetString() : null;
            var weixinName = settingsElement.TryGetProperty("weixinName", out var wn) ? wn.GetString() : null;

            Console.WriteLine($"[MainWindow] 📝 抖音URL: {douyinUrl}");
            Console.WriteLine($"[MainWindow] 视频号：{weixinName}");

            // 提取房间ID
            string? douyinRoomId = null;
            if (!string.IsNullOrEmpty(douyinUrl))
            {
                // 支持多种格式
                // 1. https://live.douyin.com/975816634199
                // 2. ws://localhost:1088/ws/975816634199
                // 3. 直接输入房间号，例如 975816634199。
                
                var match = System.Text.RegularExpressions.Regex.Match(douyinUrl, @"(\d{10,})");
                if (match.Success)
                {
                    douyinRoomId = match.Groups[1].Value;
                    Console.WriteLine($"[MainWindow] 已提取房间 ID：{douyinRoomId}");
                }
                else
                {
                    Console.WriteLine($"[MainWindow] ⚠️  无法从URL提取房间ID: {douyinUrl}");
                }
            }

            // 保存到配置文件。
            var configPath = Path.Combine(AppContext.BaseDirectory, "live_config.json");
            var config = new
            {
                douyin_room_id = douyinRoomId ?? "",
                weixin_room_id = weixinName ?? "",
                auto_start = true,
                enable_douyin = !string.IsNullOrEmpty(douyinRoomId),
                enable_weixin = !string.IsNullOrEmpty(weixinName)
            };

            var configJson = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, configJson);
            Console.WriteLine($"[MainWindow] 💾 配置已保存到: {configPath}");
            Console.WriteLine($"[MainWindow] 配置内容:");
            Console.WriteLine(configJson);

            // 停止现有连接
            if (_liveServiceManager != null)
            {
                Console.WriteLine("[MainWindow] 🛑 停止现有连接...");
                await _liveServiceManager.StopAsync();
                await Task.Delay(1500); // 等待完全停止
                Console.WriteLine("[MainWindow]  现有连接已停");
            }

            // 重新连接
            if (!string.IsNullOrEmpty(douyinRoomId))
            {
                Console.WriteLine($"[MainWindow] 正在重新连接房间：{douyinRoomId}");
                var liveConfig = new LiveConfig
                {
                    EnableDouyin = true,
                    DouyinRoomId = douyinRoomId,
                    EnableWeixin = !string.IsNullOrEmpty(weixinName),
                    WeixinRoomId = weixinName ?? "",
                    WeixinHeadless = false
                };

                await _liveServiceManager!.StartAsync(liveConfig);
                Console.WriteLine("[MainWindow] 重新连接成功");
                Console.WriteLine("========================================");

                // 通知前端
                PostMessageToWeb(JsonSerializer.Serialize(new
                {
                    type = "notification",
                    message = $"已连接到房间 {douyinRoomId}",
                    level = "success"
                }, MuyuConfig.JsonOptions));
            }
            else
            {
                Console.WriteLine("[MainWindow] ⚠️  未配置房间ID，跳过连");
                Console.WriteLine("========================================");
                
                PostMessageToWeb(JsonSerializer.Serialize(new
                {
                    type = "notification",
                    message = "设置已保存，但未配置有效的房间ID",
                    level = "warning"
                }, MuyuConfig.JsonOptions));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("========================================");
            Console.Error.WriteLine($"[MainWindow] 更新设置失败：{ex.Message}");
            Console.Error.WriteLine($"堆栈: {ex.StackTrace}");
            Console.WriteLine("========================================");

            PostMessageToWeb(JsonSerializer.Serialize(new
            {
                type = "notification",
                message = $"更新设置失败: {ex.Message}",
                level = "error"
            }, MuyuConfig.JsonOptions));
        }
    }

    private void HandleSaveConfig(JsonElement root)
    {
        var platform = root.TryGetProperty("platform", out var p) ? p.GetString() ?? "douyin" : "douyin";
        if (root.TryGetProperty("config", out var configElement))
        {
            var configJson = configElement.GetRawText();
            var config = JsonSerializer.Deserialize<PlatformMuyuConfig>(configJson, MuyuConfig.JsonOptions);
            if (config != null) _muyuService.SaveConfig(platform, config);
        }
    }

    /// <summary>
    /// 自动启动后端服务
    /// </summary>
    private async void AutoStartBackend()
    {
        try
        {
            Console.WriteLine("[MainWindow] 自动启动后端服务...");

            // 读取配置文件
            var configPath = Path.Combine(AppContext.BaseDirectory, "live_config.json");
            string? douyinRoomId = null;
            string? weixinRoomId = null;
            bool autoStart = true;
            bool enableDouyin = false;
            bool enableWeixin = false;

            if (File.Exists(configPath))
            {
                try
                {
                    var configJson = File.ReadAllText(configPath);
                    using var doc = JsonDocument.Parse(configJson);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("auto_start", out var autoStartProp))
                    {
                        autoStart = autoStartProp.GetBoolean();
                    }

                    if (root.TryGetProperty("douyin_room_id", out var roomIdProp))
                    {
                        douyinRoomId = roomIdProp.GetString();
                    }

                    if (root.TryGetProperty("weixin_room_id", out var weixinProp))
                    {
                        weixinRoomId = weixinProp.GetString();
                    }

                    if (root.TryGetProperty("enable_douyin", out var enableDouyinProp))
                    {
                        enableDouyin = enableDouyinProp.GetBoolean();
                    }

                    if (root.TryGetProperty("enable_weixin", out var enableWeixinProp))
                    {
                        enableWeixin = enableWeixinProp.GetBoolean();
                    }

                    Console.WriteLine($"[MainWindow] 配置: auto_start={autoStart}, douyin_room_id={douyinRoomId}, weixin_room_id={weixinRoomId}, enable_douyin={enableDouyin}, enable_weixin={enableWeixin}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[MainWindow] 读取配置文件失败: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine($"[MainWindow] 配置文件不存在：{configPath}");
                Console.WriteLine("[MainWindow] 使用默认房间ID: 975816634199");
                douyinRoomId = "975816634199";
                
                // 列出目录中的文件
                Console.WriteLine($"[MainWindow] 目录内容:");
                try
                {
                    foreach (var file in Directory.GetFiles(AppContext.BaseDirectory))
                    {
                        Console.WriteLine($"  - {Path.GetFileName(file)}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  无法列出文件: {ex.Message}");
                }
            }

            if (!autoStart)
            {
                Console.WriteLine("[MainWindow] 自动启动已禁");
                return;
            }

            if (!string.IsNullOrEmpty(douyinRoomId) || !string.IsNullOrEmpty(weixinRoomId))
            {
                var config = new LiveConfig
                {
                    EnableDouyin = enableDouyin && !string.IsNullOrEmpty(douyinRoomId),
                    DouyinRoomId = douyinRoomId ?? "",
                    EnableWeixin = enableWeixin && !string.IsNullOrEmpty(weixinRoomId),
                    WeixinRoomId = weixinRoomId ?? "",
                    WeixinHeadless = false
                };

                if (_liveServiceManager != null)
                {
                    await _liveServiceManager.StartAsync(config);
                    Console.WriteLine("[MainWindow] 后端服务自动启动成功");
                    Console.WriteLine("========================================");
                }
            }
            else
            {
                Console.WriteLine("[MainWindow] ⚠️  未配置任何房间ID，跳过自动启");
                Console.WriteLine("========================================");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("========================================");
            Console.Error.WriteLine($"[MainWindow] 自动启动后端失败：{ex.Message}");
            Console.Error.WriteLine($"堆栈: {ex.StackTrace}");
            Console.Error.WriteLine("========================================");
        }
    }

    private void HandleSwitchPlatform(JsonElement root)
    {
        var platform = root.TryGetProperty("platform", out var p) ? p.GetString() ?? "douyin" : "douyin";
        _muyuService.SwitchPlatform(platform);
    }

    private void HandleUploadSkin(JsonElement root)
    {
        var platform = root.TryGetProperty("platform", out var p) ? p.GetString() ?? "douyin" : "douyin";
        var skinType = root.TryGetProperty("skinType", out var st) ? st.GetString() ?? "custom" : "custom";
        var imageData = root.TryGetProperty("imageData", out var img) ? img.GetString() : null;
        _muyuService.SetSkin(platform, skinType, imageData);
    }

    private void HandlePopDanmaku()
    {
        if (_popupWindow != null && _popupWindow.IsVisible)
        {
            _popupWindow.Activate();
            return;
        }

        _popupWindow = new DanmakuPopupWindow();
        _popupWindow.Closed += (_, _) => _popupWindow = null;
        _popupWindow.Show();
    }

    private void HandleOpenMuyuOverlay(JsonElement root)
    {
        var platform = root.TryGetProperty("platform", out var p) ? p.GetString() ?? _muyuService.ActivePlatform : _muyuService.ActivePlatform;

        Console.WriteLine($"[MainWindow] HandleOpenMuyuOverlay 被调用，platform={platform}");

        // 检查该平台是否已有 overlay
        if (_muyuOverlayWindows.TryGetValue(platform, out var existing) && existing != null && existing.IsVisible)
        {
            Console.WriteLine($"[MainWindow] {platform} overlay 窗口已存在且可见，激");
            existing.Activate();
            return;
        }

        Console.WriteLine($"[MainWindow] 创建新的 {platform} MuyuOverlayWindow...");
        var overlayWindow = new MuyuOverlayWindow(platform);
        _muyuOverlayWindows[platform] = overlayWindow;

        overlayWindow.WebViewReady += (_, _) =>
        {
            Console.WriteLine($"[MainWindow] {platform} overlay WebViewReady 已触发");
            if (overlayWindow.OverlayWebView != null)
            {
                _popupWebViews.Add(overlayWindow.OverlayWebView);
                Console.WriteLine($"[MainWindow] 木鱼叠加 WebView2 已注册，popupWvCount={_popupWebViews.Count}");
            }
        };
        overlayWindow.Closed += (_, _) =>
        {
            Console.WriteLine($"[MainWindow] {platform} overlay 窗口关闭");
            if (overlayWindow.OverlayWebView != null)
            {
                _popupWebViews.Remove(overlayWindow.OverlayWebView);
            }
            _muyuOverlayWindows.Remove(platform);
        };
        overlayWindow.Show();
        Console.WriteLine($"[MainWindow] {platform} overlay 窗口已显示");
    }


    /// <summary>
    /// 打开微信视频号 WebView2 登录窗口。
    /// </summary>
    private void HandleOpenWeixinWebView()
    {
        try
        {
            if (_weixinLoginWindow?.IsVisible == true)
            {
                _weixinLoginWindow.Activate();
                return;
            }
            Console.WriteLine("[MainWindow] 打开视频号登录窗口");
            _weixinLoginWindow = new WeixinLoginWindow();
            _weixinLoginWindow.Closed += (_, _) => _weixinLoginWindow = null;
            _weixinLoginWindow.Show(this);
        }
        catch (Exception ex)
        {
            _logger.Error($"打开微信登录窗口失败: {ex.Message}", ex);
            Console.Error.WriteLine($"[MainWindow] 打开微信登录窗口失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 打开独立的 WebView2 弹幕保存窗口，并自动启动 douyinLive 服务。
    /// 抖音弹幕数据来源: https://github.com/jwwsjlm/douyinLive (MIT License)
    /// </summary>
    private async void HandleOpenDanmuSave()
    {
        try
        {
            Console.WriteLine("[MainWindow] 正在创建弹幕保存窗口...");

            // 自动启动 douyinLive 服务
            StartDouyinLiveExe();

            var popupWindow = new Window
            {
                Title = "💾 弹幕保存 - 多直播间监控",
                Width = 920,
                Height = 720,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
            };

            var popupWebView = new Avalonia.Controls.WebView2();
            popupWindow.Content = popupWebView;
            popupWindow.Show();

            var popupEnv = await Services.AppPaths.CreateWebView2EnvironmentAsync();
            await popupWebView.EnsureCoreWebView2Async(popupEnv);

            // 绑定消息通道
            popupWebView.WebMessageReceived += (_, pArgs) =>
            {
                var pJson = pArgs.WebMessageAsJson;
                string pMessage;
                try { pMessage = JsonSerializer.Deserialize<string>(pJson) ?? pJson; }
                catch { pMessage = pJson; }
                Console.WriteLine($"[DanmuSave] 收到消息: {pMessage}");
                Dispatcher.UIThread.Post(() => OnWebMessageReceived(pMessage));
            };

            // 导航到弹幕保存页面。
            var htmlPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "danmu-save.html");
            popupWebView.Source = new Uri("file:///" + htmlPath.Replace('\\', '/'));

            // 注册到弹窗列表。
            _popupWebViews.Add(popupWebView);

            // 窗口关闭时清理资源。
            popupWindow.Closed += (_, _) =>
            {
                _popupWebViews.Remove(popupWebView);
                Console.WriteLine("[MainWindow] 弹幕保存窗口已关");
            };

            Console.WriteLine("[MainWindow]  弹幕保存窗口已创");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MainWindow] 创建弹幕保存窗口失败: {ex.Message}");
            _logger.Error($"创建弹幕保存窗口失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 启动 douyinLive-windows-amd64.exe 服务
    /// 来源: https://github.com/jwwsjlm/douyinLive (MIT License)
    /// 启动后提供 ws://127.0.0.1:1088/ws/{roomId} WebSocket 服务。
    /// </summary>
    private void StartDouyinLiveExe()
    {
        try
        {
            // 检查内部进程引用。
            if (_douyinLiveProcess != null)
            {
                try
                {
                    if (!_douyinLiveProcess.HasExited)
                    {
                        Console.WriteLine($"[douyinLive] 服务已在运行, PID={_douyinLiveProcess.Id}");
                        return;
                    }
                }
                catch { /* 进程已退出 */ }
                _douyinLiveProcess = null;
            }

            // 检查系统中是否已有同名进程在跑
            var existing = Process.GetProcessesByName("douyinLive-windows-amd64");
            if (existing.Length > 0)
            {
                Console.WriteLine($"[douyinLive] 系统中已有 {existing.Length} 个进程正在运行，PID={existing[0].Id}");
                _douyinLiveProcess = existing[0];  // 复用已有进程
                return;
            }

            // 查找 exe 路径 - 支持多个位置
            string? exePath = null;
            string? exeDir = null;
            var candidates = new[]
            {
                // 1. 发布目录
                Path.Combine(AppContext.BaseDirectory, "douyinLive-windows-amd64.exe", "douyinLive-windows-amd64.exe"),
                // 2. 发布目录同级
                Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)) ?? "", "douyinLive-windows-amd64.exe", "douyinLive-windows-amd64.exe"),
                // 3. 项目根目录
                Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)) ?? "") ?? "", "douyinLive-windows-amd64.exe", "douyinLive-windows-amd64.exe"),
            };

            foreach (var candidate in candidates)
            {
                Console.WriteLine($"[douyinLive] 检查路径：{candidate}");
                if (File.Exists(candidate))
                {
                    exePath = candidate;
                    exeDir = Path.GetDirectoryName(candidate);
                    break;
                }
            }

            if (exePath == null || exeDir == null)
            {
                _logger.Error("[douyinLive] 找不到 douyinLive-windows-amd64.exe");
                Console.Error.WriteLine($"[douyinLive] 找不到可执行文件，已检查：{string.Join(", ", candidates)}");
                return;
            }

            Console.WriteLine($"[douyinLive] 启动服务: {exePath}");

            _douyinLiveProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = exeDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
                EnableRaisingEvents = true
            };

            _douyinLiveProcess.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Console.WriteLine($"[douyinLive] {e.Data}");
            };
            _douyinLiveProcess.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Console.Error.WriteLine($"[douyinLive-ERR] {e.Data}");
            };
            _douyinLiveProcess.Exited += (_, _) =>
            {
                Console.WriteLine("[douyinLive] 服务进程已退");
                _douyinLiveProcess = null;
            };

            _douyinLiveProcess.Start();
            _douyinLiveProcess.BeginOutputReadLine();
            _douyinLiveProcess.BeginErrorReadLine();

            _logger.Info($"[douyinLive] 服务已启动，PID={_douyinLiveProcess.Id}");
            Console.WriteLine($"[douyinLive] 服务已启动，PID={_douyinLiveProcess.Id}");
        }
        catch (Exception ex)
        {
            _logger.Error($"[douyinLive] 启动失败: {ex.Message}", ex);
            Console.Error.WriteLine($"[douyinLive] 启动失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 停止 douyinLive 服务
    /// </summary>
    private void StopDouyinLiveExe()
    {
        try
        {
            if (_douyinLiveProcess != null && !_douyinLiveProcess.HasExited)
            {
                Console.WriteLine($"[douyinLive] 停止服务, PID={_douyinLiveProcess.Id}");
                _douyinLiveProcess.Kill(true);
                _douyinLiveProcess.Dispose();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[douyinLive] 停止失败: {ex.Message}");
        }
        finally
        {
            _douyinLiveProcess = null;
        }
    }

    /// <summary>
    /// 启动多直播间抖音监听（弹幕保存模块）
    /// </summary>
    private async void HandleStartMultiRoomMonitor(JsonElement root)
    {
        try
        {
            // 先停止之前的额外监控
            await StopExtraDouyinMonitors();

            if (!root.TryGetProperty("roomIds", out var roomIdsElem))
            {
                _logger.Error("[MultiRoom] 缺少 roomIds");
                return;
            }

            var cookieManager = new CookieManager(_logger);
            var messageAggregator = new MessageAggregator();

            // 订阅消息并转发到前端弹幕保存窗口。
            messageAggregator.MessageReceived += (_, msg) =>
            {
                PushDanmuToSaveBuffer(msg);
            };

            foreach (var roomIdElem in roomIdsElem.EnumerateArray())
            {
                var roomId = roomIdElem.GetString();
                if (string.IsNullOrWhiteSpace(roomId)) continue;

                _logger.Info($"[MultiRoom] 启动抖音监控，房间：{roomId}");

                var service = new DouyinDirectService(messageAggregator, _logger, cookieManager);
                service.StatusChanged += (s, status) =>
                {
                    _logger.Info($"[MultiRoom] 房间 {roomId}: {status}");
                };

                _extraDouyinMonitors.Add(service);

                // 异步启动每个房间
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await service.StartAsync(roomId);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"[MultiRoom] 房间 {roomId} 启动失败: {ex.Message}", ex);
                    }
                });
            }

            _logger.Info($"[MultiRoom] 启动 {_extraDouyinMonitors.Count} 个额外抖音监");
        }
        catch (Exception ex)
        {
            _logger.Error($"[MultiRoom] 启动失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 停止多直播间抖音监听
    /// </summary>
    private async void HandleStopMultiRoomMonitor()
    {
        await StopExtraDouyinMonitors();
    }

    private async Task StopExtraDouyinMonitors()
    {
        foreach (var service in _extraDouyinMonitors)
        {
            try
            {
                await service.StopAsync();
                service.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Error($"[MultiRoom] 停止服务失败: {ex.Message}", ex);
            }
        }
        _extraDouyinMonitors.Clear();
        _logger.Info("[MultiRoom] 已停止所有额外抖音监");
    }

    /// <summary>
    /// 将弹幕消息推送到弹幕保存窗口
    /// </summary>
    private void PushDanmuToSaveBuffer(LiveMessage msg)
    {
        if (_popupWebViews.Count == 0) return;

        var escapedUser = (msg.Username ?? "").Replace("\\", "\\\\").Replace("'", "\\'");
        var escapedContent = (msg.Content ?? "").Replace("\\", "\\\\").Replace("'", "\\'");
        var platform = (msg.Platform ?? "douyin").Replace("'", "\\'");

        var js = $@"
            if (window.onDanmuSaveMessage) {{
                window.onDanmuSaveMessage({{
                    platform: '{platform}',
                    msgType: '{msg.MsgType}',
                    username: '{escapedUser}',
                    content: '{escapedContent}'
                }});
            }}
        ";

        Dispatcher.UIThread.Post(() =>
        {
            foreach (var popupWv in _popupWebViews.ToArray())
            {
                try { popupWv.CoreWebView2?.ExecuteScriptAsync(js); }
                catch { /* ignore */ }
            }
        });
    }

    /// <summary>
    /// 启动弹幕保存专用的视频号嗅探器，通过代理注入脚本监听微信 PC 端视频号弹幕。
    /// 参考：https://github.com/ltaoo/wx_channels_download
    /// </summary>
    private async void HandleStartWeixinSniffer()
    {
        try
        {
            if (_danmuWeixinSniffer != null && _danmuWeixinSniffer.IsRunning)
            {
                _logger.Warn("[WeixinSniffer-DanmuSave] 嵅探已在运行");
                PushStatusToPopup("weixin-sniffer-status", "running", "视频号嵅探已在运");
                return;
            }

            // 创建独立的 MessageAggregator，不与主服务或木鱼模块共享。
            var messageAggregator = new MessageAggregator();
            messageAggregator.MessageReceived += (_, msg) =>
            {
                PushDanmuToSaveBuffer(msg);
            };

            _danmuWeixinSniffer = new WeixinSnifferService(messageAggregator, _logger);
            _danmuWeixinSniffer.StatusChanged += (_, status) =>
            {
                _logger.Info($"[WeixinSniffer-DanmuSave] {status}");
                PushStatusToPopup("weixin-sniffer-status", "info", status);
            };

            await _danmuWeixinSniffer.StartAsync();
            _logger.Info("[WeixinSniffer-DanmuSave] 视频号嵅探已启动（代 JS注入方式");
            PushStatusToPopup("weixin-sniffer-status", "running", "视频号嵅探已启动，请在微信中打开视频号直");
        }
        catch (Exception ex)
        {
            _logger.Error($"[WeixinSniffer-DanmuSave] 启动失败: {ex.Message}", ex);
            PushStatusToPopup("weixin-sniffer-status", "error", $"启动失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 停止弹幕保存专用的视频号嵅探
    /// </summary>
    private async void HandleStopWeixinSniffer()
    {
        try
        {
            if (_danmuWeixinSniffer != null)
            {
                await _danmuWeixinSniffer.StopAsync();
                _danmuWeixinSniffer.Dispose();
                _danmuWeixinSniffer = null;
                _logger.Info("[WeixinSniffer-DanmuSave] 视频号嵅探已停止");
                PushStatusToPopup("weixin-sniffer-status", "stopped", "视频号嵅探已停止");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"[WeixinSniffer-DanmuSave] 停止失败: {ex.Message}", ex);
        }
    }

    // ================================================================
    //               幸运九宫格抽奖
    // ================================================================

    /// <summary>
    /// 启动九宫格抽奖模式（和木鱼互斥）
    /// </summary>
    private void HandleStartLotteryMode()
    {
        _lotteryMode = true;
        _logger.Info("[Lottery] 九宫格抽奖模式已启动，礼物消息将转发到抽奖模块");

        Dispatcher.UIThread.Post(() =>
        {
            var js = "if(window.onLotteryStatus){window.onLotteryStatus('connected');}";
            _webView?.CoreWebView2?.ExecuteScriptAsync(js);
        });
    }

    /// <summary>
    /// 停止九宫格抽奖模式。
    /// </summary>
    private void HandleStopLotteryMode()
    {
        _lotteryMode = false;
        _logger.Info("[Lottery] 九宫格抽奖模式已停止");

        Dispatcher.UIThread.Post(() =>
        {
            var js = "if(window.onLotteryStatus){window.onLotteryStatus('disconnected');}";
            _webView?.CoreWebView2?.ExecuteScriptAsync(js);
        });
    }

    /// <summary>
    /// 打开九宫格 overlay 窗口，供 OBS 采集。
    /// </summary>
    private void HandleOpenLotteryOverlay(JsonElement root)
    {
        // 提前提取配置 JSON，避免 JsonDocument 释放后被异步访问。
        string? configJsonSnapshot = null;
        try
        {
            if (root.TryGetProperty("config", out var config))
            {
                configJsonSnapshot = config.GetRawText();
            }
        }
        catch { /* root 可能已经失效 */ }

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var window = new Window
                {
                    Title = "幸运九宫 - OBS ",
                    Width = 520,
                    Height = 580,
                    Background = Avalonia.Media.Brushes.Transparent,
                    TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
                    SystemDecorations = SystemDecorations.Full
                };

                var webView = new Avalonia.Controls.WebView2();
                window.Content = webView;
                window.Show();

                await webView.EnsureCoreWebView2Async();
                if (webView.CoreWebView2 != null)
                {
                    webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;

                    var exeDir = AppContext.BaseDirectory;
                    var htmlPath = System.IO.Path.Combine(exeDir, "wwwroot", "lucky-grid.html");
                    webView.CoreWebView2.Navigate($"file:///{htmlPath.Replace('\\', '/')}");

                    // BUG FIX: 使用 TryGetWebMessageAsString 而非 WebMessageAsJson 避免双重编码
                    webView.CoreWebView2.WebMessageReceived += (s, e) =>
                    {
                        try
                        {
                            var rawMsg = e.TryGetWebMessageAsString();
                            if (string.IsNullOrEmpty(rawMsg)) return;

                            using var doc = JsonDocument.Parse(rawMsg);
                            var msgRoot = doc.RootElement;
                            if (msgRoot.TryGetProperty("type", out var t) && t.GetString() == "lottery-result")
                            {
                                var prize = msgRoot.TryGetProperty("prize", out var p) ? p.GetString() : "未知";
                                var emoji = msgRoot.TryGetProperty("emoji", out var em) ? em.GetString() : "🎁";
                                var user = msgRoot.TryGetProperty("username", out var u) ? u.GetString() : "手动";
                                _logger.Info($"[Lottery] 抽奖结果: {emoji} {prize} (用户: {user})");

                                Dispatcher.UIThread.Post(() =>
                                {
                                    var resultJs = $"if(window.onLotteryResult){{window.onLotteryResult({{emoji:'{emoji}',prize:'{(prize ?? "").Replace("'", "\\'")}',username:'{(user ?? "").Replace("'", "\\'")}'}});}}";
                                    _webView?.CoreWebView2?.ExecuteScriptAsync(resultJs);
                                });
                            }
                        }
                        catch (Exception ex) { _logger.Error($"[Lottery] overlay消息处理失败: {ex.Message}"); }
                    };

                    _lotteryOverlayWebView = webView;

                    // 使用预先提取的 configJsonSnapshot。
                    if (!string.IsNullOrEmpty(configJsonSnapshot))
                    {
                        var cfgJson = configJsonSnapshot;
                        _ = Task.Delay(1500).ContinueWith(_ =>
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                webView.CoreWebView2?.ExecuteScriptAsync($"if(window.onHostConfig){{window.onHostConfig({cfgJson});}}");
                            });
                        });
                    }
                }

                window.Closed += (s, e) => { _lotteryOverlayWebView = null; };
                _logger.Info("[Lottery] Overlay 窗口已打开");
            }
            catch (Exception ex)
            {
                _logger.Error($"[Lottery] 打开 overlay 失败: {ex.Message}", ex);
            }
        });
    }

    /// <summary>
    /// 手动触发抽奖
    /// </summary>
    private void HandleLotteryManualSpin()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_lotteryOverlayWebView?.CoreWebView2 != null)
            {
                _lotteryOverlayWebView.CoreWebView2.ExecuteScriptAsync("if(window.onHostSpin){window.onHostSpin();}");
                _logger.Info("[Lottery] 已触发手动抽");
            }
            else
            {
                _logger.Warn("[Lottery] Overlay 未打开，无法触发抽");
            }
        });
    }

    /// <summary>
    /// 更新九宫格配置到 overlay
    /// </summary>
    private void HandleLotteryConfigUpdate(JsonElement root)
    {
        if (!root.TryGetProperty("config", out var config)) return;

        if (config.TryGetProperty("triggerGift", out var tg))
        {
            _lotteryTriggerGift = tg.GetString() ?? "";
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_lotteryOverlayWebView?.CoreWebView2 != null)
            {
                var configJson = config.GetRawText();
                _lotteryOverlayWebView.CoreWebView2.ExecuteScriptAsync($"if(window.onHostConfig){{window.onHostConfig({configJson});}}");
            }
        });
    }

    /// <summary>
    /// 九宫格模式下处理礼物消息，并转发到 overlay 触发抽奖。
    /// </summary>
    private void HandleLotteryGiftMessage(LiveMessage message)
    {
        var giftName = message.Content ?? "";
        var username = message.Username ?? "匿名";

        if (!string.IsNullOrEmpty(_lotteryTriggerGift) && !giftName.Contains(_lotteryTriggerGift))
        {
            _logger.Debug($"[Lottery] 礼物 '{giftName}' 不匹配触发条 '{_lotteryTriggerGift}'，忽");
            return;
        }

        _logger.Info($"[Lottery] 收到礼物触发抽奖: {username} 送出 {giftName}");

        Dispatcher.UIThread.Post(() =>
        {
            if (_lotteryOverlayWebView?.CoreWebView2 != null)
            {
                var escapedUser = (username).Replace("'", "\\'");
                var escapedGift = (giftName).Replace("'", "\\'");
                _lotteryOverlayWebView.CoreWebView2.ExecuteScriptAsync(
                    $"if(window.onHostGift){{window.onHostGift('{escapedUser}','{escapedGift}');}}");
            }
        });
    }

    // ================================================================
    //               像素问号弹幕抽奖
    // ================================================================

    private void HandleStartMysteryMode(JsonElement root)
    {
        _mysteryMode = true;
        _muyuService.SuppressMuyuHit = true;  // 抑制木鱼敲击，但保留弹幕显示。
        if (root.TryGetProperty("platform", out var p))
            _mysteryPlatform = p.GetString() ?? "douyin";
        _logger.Info($"[Mystery] 像素问号模式已启动，平台：{_mysteryPlatform}，木鱼已暂停");

        Dispatcher.UIThread.Post(() =>
        {
            var js = "if(window.onMysteryStatus){window.onMysteryStatus('connected');}";
            _webView?.CoreWebView2?.ExecuteScriptAsync(js);
        });
    }

    private void HandleStopMysteryMode()
    {
        _mysteryMode = false;
        _muyuService.SuppressMuyuHit = false;  // 恢复木鱼
        _logger.Info("[Mystery] 像素问号模式已停止，木鱼已恢");

        Dispatcher.UIThread.Post(() =>
        {
            var js = "if(window.onMysteryStatus){window.onMysteryStatus('disconnected');}";
            _webView?.CoreWebView2?.ExecuteScriptAsync(js);
        });
    }

    private void HandleOpenMysteryOverlay(JsonElement root)
    {
        string? configJsonSnapshot = null;
        try
        {
            if (root.TryGetProperty("config", out var config))
                configJsonSnapshot = config.GetRawText();
        }
        catch { }

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var platformLabel = _mysteryPlatform == "weixin" ? "视频号" : "抖音";
                var window = new Window
                {
                    Title = $"像素问号 - OBS 窗口（{platformLabel}）",
                    Width = 480,
                    Height = 560,
                    Background = Avalonia.Media.Brushes.Transparent,
                    TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
                    SystemDecorations = SystemDecorations.Full
                };

                var webView = new Avalonia.Controls.WebView2();
                window.Content = webView;
                window.Show();

                await webView.EnsureCoreWebView2Async();
                if (webView.CoreWebView2 != null)
                {
                    // 将 WebView2 背景设为透明，供 OBS 透明源采集。
                    webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;

                    var exeDir = AppContext.BaseDirectory;
                    var htmlPath = System.IO.Path.Combine(exeDir, "wwwroot", "pixel-mystery.html");
                    webView.CoreWebView2.Navigate($"file:///{htmlPath.Replace('\\', '/')}");

                    webView.CoreWebView2.WebMessageReceived += (s, e) =>
                    {
                        try
                        {
                            var rawMsg = e.TryGetWebMessageAsString();
                            if (string.IsNullOrEmpty(rawMsg)) return;

                            using var doc = JsonDocument.Parse(rawMsg);
                            var msgRoot = doc.RootElement;
                            if (msgRoot.TryGetProperty("type", out var t) && t.GetString() == "mystery-result")
                            {
                                var username = msgRoot.TryGetProperty("username", out var u) ? u.GetString() : "未知";
                                var prize = msgRoot.TryGetProperty("prize", out var pr) ? pr.GetString() : "?";
                                var avatar = msgRoot.TryGetProperty("avatar", out var av) ? av.GetString() : "";
                                _logger.Info($"[Mystery] 抽奖结果：{username} - {prize}");

                                Dispatcher.UIThread.Post(() =>
                                {
                                    var resultJs = $"if(window.onMysteryResult){{window.onMysteryResult({{username:'{(username ?? "").Replace("'", "\\'")}',prize:'{(prize ?? "").Replace("'", "\\'")}',avatar:'{(avatar ?? "").Replace("'", "\\'")}'}}); }}";
                                    _webView?.CoreWebView2?.ExecuteScriptAsync(resultJs);
                                });
                            }
                        }
                        catch (Exception ex) { _logger.Error($"[Mystery] overlay消息处理失败: {ex.Message}"); }
                    };

                    _mysteryOverlayWebView = webView;

                    if (!string.IsNullOrEmpty(configJsonSnapshot))
                    {
                        var cfgJson = configJsonSnapshot;
                        _ = Task.Delay(1500).ContinueWith(_ =>
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                webView.CoreWebView2?.ExecuteScriptAsync($"if(window.onHostConfig){{window.onHostConfig({cfgJson});}}");
                            });
                        });
                    }
                }

                window.Closed += (s, e) => { _mysteryOverlayWebView = null; };
                _logger.Info("[Mystery] Overlay 窗口已打开");
            }
            catch (Exception ex)
            {
                _logger.Error($"[Mystery] 打开 overlay 失败: {ex.Message}", ex);
            }
        });
    }

    private void HandleMysteryManualDraw()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_mysteryOverlayWebView?.CoreWebView2 != null)
            {
                _mysteryOverlayWebView.CoreWebView2.ExecuteScriptAsync("if(window.onHostDraw){window.onHostDraw();}");
                _logger.Info("[Mystery] 已触发手动抽");
            }
            else
            {
                _logger.Warn("[Mystery] Overlay 未打开，无法触发抽");
            }
        });
    }

    private void HandleMysteryConfigUpdate(JsonElement root)
    {
        if (!root.TryGetProperty("config", out var config)) return;

        if (config.TryGetProperty("keyword", out var kw))
            _mysteryKeyword = kw.GetString() ?? "抽奖";

        // 提前提取 JSON 快照，避免 JsonDocument 释放后被异步访问。
        var configJson = config.GetRawText();

        Dispatcher.UIThread.Post(() =>
        {
            if (_mysteryOverlayWebView?.CoreWebView2 != null)
            {
                _mysteryOverlayWebView.CoreWebView2.ExecuteScriptAsync($"if(window.onHostConfig){{window.onHostConfig({configJson});}}");
            }
        });
    }

    private void HandleMysteryClearPool()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _mysteryOverlayWebView?.CoreWebView2?.ExecuteScriptAsync("if(window.state){window.state.pool=[];}");
            _logger.Info("[Mystery] 参与者池已清");
        });
    }

    private void HandleMysteryDanmakuMessage(LiveMessage message)
    {
        var content = message.Content ?? "";
        var username = message.Username ?? "匿名";
        var avatar = message.AvatarUrl ?? "";

        // 调试：打印 extra_data 的所有键，帮助定位头像字段。
        if (message.ExtraData != null && message.ExtraData.Count > 0)
        {
            var keys = string.Join(", ", message.ExtraData.Keys);
            _logger.Debug($"[Mystery] extra_data keys: {keys}");
            
            // 尝试更多可能的头像字段。
            if (string.IsNullOrEmpty(avatar))
            {
                foreach (var key in new[] { "avatar_thumb", "avatarThumb", "user_avatar", "head_img", "head_url", "icon" })
                {
                    if (message.ExtraData.TryGetValue(key, out var v))
                    {
                        var val = v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : v.GetRawText();
                        if (!string.IsNullOrEmpty(val))
                        {
                            avatar = val;
                            _logger.Debug($"[Mystery] 从 extra_data[{key}] 获取头像：{avatar}");
                            break;
                        }
                    }
                }
            }
        }

        // 检查关键字
        if (!string.IsNullOrEmpty(_mysteryKeyword) && !content.Contains(_mysteryKeyword))
            return;

        _logger.Info($"[Mystery] 弹幕匹配：{username} 发送“{content}”，avatar={avatar}");

        var time = DateTime.Now.ToString("HH:mm:ss");

        // 转发到 overlay。
        Dispatcher.UIThread.Post(() =>
        {
            if (_mysteryOverlayWebView?.CoreWebView2 != null)
            {
                var eu = (username).Replace("'", "\\'");
                var ec = (content).Replace("'", "\\'");
                var ea = (avatar).Replace("'", "\\'");
                _mysteryOverlayWebView.CoreWebView2.ExecuteScriptAsync(
                    $"if(window.onHostDanmaku){{window.onHostDanmaku('{eu}','{ec}','{ea}','{time}');}}");
            }

            // 同时通知前端 UI 更新参与者池
            if (_webView?.CoreWebView2 != null)
            {
                var eu2 = (username).Replace("'", "\\'");
                var ea2 = (avatar).Replace("'", "\\'");
                _webView.CoreWebView2.ExecuteScriptAsync(
                    $"if(window.onMysteryDanmaku){{window.onMysteryDanmaku({{username:'{eu2}',avatar:'{ea2}',time:'{time}'}});}}");
            }
        });
    }

    /// <summary>
    /// 推送状态信息到弹幕保存弹窗
    /// </summary>
    private void PushStatusToPopup(string type, string status, string text)
    {
        if (_popupWebViews.Count == 0) return;
        var escapedText = (text ?? "").Replace("\\", "\\\\").Replace("'", "\\'");
        var js = $"if(window.onHostStatus){{window.onHostStatus('{type}','{status}','{escapedText}');}}";
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var wv in _popupWebViews.ToArray())
            {
                try { wv.CoreWebView2?.ExecuteScriptAsync(js); }
                catch { }
            }
        });
    }

    /// <summary>
    /// 处理前端窗口拖拽，通过鼠标屏幕坐标增量移动窗口。
    /// </summary>
    private void HandleDragMove(JsonElement root)
    {
        if (root.TryGetProperty("dx", out var dxProp) &&
            root.TryGetProperty("dy", out var dyProp))
        {
            var dx = (int)dxProp.GetDouble();
            var dy = (int)dyProp.GetDouble();
            Position = new PixelPoint(Position.X + dx, Position.Y + dy);
        }
    }

    /// <summary>
    /// 向前端推送 JSON 消息。
    /// </summary>
    public void PostMessageToWeb(string json)
    {
        if (!_webViewReady && _popupWebViews.Count == 0) return;

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
                // 使用 try-catch 包裹前端执行，以捕获 JavaScript 异常。
                var script = $@"
                    try {{
                        if(window.onHostMessage) {{
                            window.onHostMessage(JSON.parse(atob('{b64}')));
                            'ok';
                        }} else {{
                            'no-handler';
                        }}
                    }} catch(e) {{
                        'error:' + e.message;
                    }}";

                // 推送到主窗口。
                if (_webViewReady && _webView != null)
                {
                    try
                    {
                        var result = await _webView.ExecuteScriptAsync(script);
                        // 提取消息类型用于日志
                        var typeStart = json.IndexOf("\"type\":");
                        var msgType = typeStart >= 0 ? json.Substring(typeStart, Math.Min(30, json.Length - typeStart)) : "?";
                        if (result != null && result != "\"ok\"")
                        {
                            Console.WriteLine($"[PostMessageToWeb] ⚠️ JS返回: result={result}, msg={msgType}");
                        }
                        else if (json.Contains("muyu-hit"))
                        {
                            Console.WriteLine($"[PostMessageToWeb] muyu-hit 已推送到主 WebView，result={result}");
                        }
                        else if (json.Contains("danmaku"))
                        {
                            Console.WriteLine($"[PostMessageToWeb] danmaku 已推送到主 WebView，result={result}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"[PostMessageToWeb] 主窗口执行失败：{ex.Message}");
                    }
                }

                // 同时推送到所有子窗口，包括木鱼叠加和弹幕保存窗口。
                foreach (var popupWv in _popupWebViews.ToArray())
                {
                    try { popupWv.CoreWebView2?.ExecuteScriptAsync(script); }
                    catch { /* ignore */ }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[PostMessageToWeb] 编码或推送失败：{ex.Message}");
            }
        });
    }

    /// <summary>
    /// 处理服务状态变更。
    /// </summary>
    private void OnServiceStatusChanged(object? sender, ServiceStatus status)
    {
        var statusMsg = new
        {
            type = "ws-status",
            platform = status.Platform,
            status = status.IsConnected ? "connected" : "disconnected",
            text = status.Message
        };
        PostMessageToWeb(JsonSerializer.Serialize(statusMsg, MuyuConfig.JsonOptions));
    }

    /// <summary>
    /// 打开抖音登录窗口
    /// </summary>
    private void HandleOpenDouyinLogin()
    {
        try
        {
            if (_douyinLoginWindow?.IsVisible == true)
            {
                _douyinLoginWindow.Activate();
                return;
            }
            Console.WriteLine("[MainWindow] 打开抖音登录窗口");
            
            _douyinLoginWindow = new DouyinLoginWindow();
            _douyinLoginWindow.Closed += (_, _) => _douyinLoginWindow = null;
            _douyinLoginWindow.Show(this);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MainWindow] 打开抖音登录窗口失败: {ex}");
        }
    }

    /// <summary>
    /// 窗口关闭时清理资源。
    /// </summary>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (!_isClosing)
        {
            _isClosing = true;
            
            // 立即隐藏窗口，让用户感觉关闭很快
            this.Hide();
            
            // 带超时的后台清理
            try
            {
                var cleanupTask = Task.Run(async () =>
                {
                    // 停止微信嗅探器。
                    // 停止弹幕保存专用视频号嗅探器。
                    if (_danmuWeixinSniffer != null)
                    {
                        try { await _danmuWeixinSniffer.StopAsync(); }
                        catch { /* ignore */ }
                    }

                    // 停止 douyinLive 外部服务
                    StopDouyinLiveExe();

                    // 停止多直播间监控
                    await StopExtraDouyinMonitors();

                    // 停止主服务。
                    if (_liveServiceManager != null)
                    {
                        try
                        {
                            await _liveServiceManager.StopAsync();
                            _liveServiceManager.Dispose();
                        }
                        catch { /* ignore */ }
                    }
                });

                // 最多等待 3 秒完成清理。
                await Task.WhenAny(cleanupTask, Task.Delay(3000));
            }
            catch { /* ignore */ }

            // 关闭所有弹窗。
            foreach (var wv in _popupWebViews.ToArray())
            {
                try { wv.Dispose(); } catch { }
            }
            _popupWebViews.Clear();

            // 释放 WebView2。
            try { _webView?.Dispose(); } catch { }
        }
        
        base.OnClosing(e);

        // 确保进程退出（防止后台线程阻止退出）
        _ = Task.Delay(2000).ContinueWith(_ => Environment.Exit(0));
    }
    
    private bool _isClosing = false;
}
