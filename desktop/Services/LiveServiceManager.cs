using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using LiveDanmuDesktop.Models;

namespace LiveDanmuDesktop.Services;

public sealed class LiveServiceManager : IDisposable
{
    private readonly DouyinDirectService _douyinService;
    private readonly WeixinLiveService _weixinService;
    private readonly MessageAggregator _messageAggregator;
    private readonly CookieManager _cookieManager;
    private readonly Logger _logger;
    private readonly ConfigService _configService;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private bool _disposed;

    public CookieManager CookieManager => _cookieManager;
    public event EventHandler<LiveMessage>? MessageReceived;
    public event EventHandler<ServiceStatus>? StatusChanged;

    public LiveServiceManager(Logger logger)
    {
        _logger = logger;
        _configService = new ConfigService();
        _cookieManager = new CookieManager(logger);
        _messageAggregator = new MessageAggregator();
        _douyinService = new DouyinDirectService(_messageAggregator, _logger, _cookieManager);
        _weixinService = new WeixinLiveService(_messageAggregator, _logger, _cookieManager);
        _messageAggregator.MessageReceived += OnMessageReceived;
        _douyinService.StatusChanged += (_, message) => OnStatusChanged("douyin", message);
        _weixinService.StatusChanged += (_, message) => OnStatusChanged("weixin", message);
    }

    public async Task StartAsync(LiveConfig? config = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycleLock.WaitAsync();
        try
        {
            config ??= _configService.Load();
            _logger.Info("启动直播监控服务...");

            var startTasks = new List<Task>(2);
            if (config.EnableDouyin && !string.IsNullOrWhiteSpace(config.DouyinRoomId))
                startTasks.Add(StartDouyinIsolatedAsync(config.DouyinRoomId));
            if (config.EnableWeixin && !string.IsNullOrWhiteSpace(config.WeixinRoomId))
                startTasks.Add(StartWeixinIsolatedAsync(config.WeixinRoomId, config.WeixinHeadless));
            await Task.WhenAll(startTasks);

            _logger.Info("直播监控服务启动流程完成");
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private async Task StartDouyinIsolatedAsync(string roomId)
    {
        try
        {
            _logger.Info($"启动抖音服务（独立任务），房间 ID: {roomId}");
            await _douyinService.StartAsync(roomId);
        }
        catch (Exception ex)
        {
            _logger.Error("启动抖音服务失败: " + ex.Message, ex);
            OnStatusChanged("douyin", "连接失败: " + ex.Message);
        }
    }

    private async Task StartWeixinIsolatedAsync(string roomId, bool headless)
    {
        try
        {
            _logger.Info($"启动视频号服务（独立任务），房间 ID: {roomId}");
            await _weixinService.StartAsync(roomId, headless);
        }
        catch (Exception ex)
        {
            _logger.Error("启动视频号服务失败: " + ex.Message, ex);
            OnStatusChanged("weixin", "启动失败: " + ex.Message);
        }
    }

    public async Task StopAsync()
    {
        if (_disposed) return;
        await _lifecycleLock.WaitAsync();
        try
        {
            _logger.Info("停止直播监控服务...");
            await StopServiceAsync("douyin", "抖音", _douyinService.StopAsync);
            await StopServiceAsync("weixin", "视频号", _weixinService.StopAsync);
            _logger.Info("直播监控服务已停止");
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private async Task StopServiceAsync(string platform, string serviceName, Func<Task> stopAsync)
    {
        try
        {
            await stopAsync();
        }
        catch (Exception ex)
        {
            _logger.Error($"停止{serviceName}服务失败: {ex.Message}", ex);
            OnStatusChanged(platform, "停止失败: " + ex.Message);
        }
    }

    public async Task RestartAsync(LiveConfig? config = null)
    {
        await StopAsync();
        await Task.Delay(300);
        await StartAsync(config);
    }

    public (bool douyinRunning, bool weixinRunning) GetStatus() =>
        (_douyinService.IsRunning, _weixinService.IsRunning);

    public Task RequestWeixinLoginAsync() => _weixinService.RequestLoginAsync();

    private void OnMessageReceived(object? sender, LiveMessage message)
    {
        try
        {
            MessageReceived?.Invoke(this, message);
        }
        catch (Exception ex)
        {
            _logger.Error("处理消息事件失败: " + ex.Message, ex);
        }
    }

    private void OnStatusChanged(string platform, string message)
    {
        try
        {
            var isConnected = message.Contains("已连接", StringComparison.Ordinal) ||
                              message.Contains("已重新连接", StringComparison.Ordinal);
            StatusChanged?.Invoke(this, new ServiceStatus
            {
                Platform = platform,
                IsConnected = isConnected,
                Message = message
            });
        }
        catch (Exception ex)
        {
            _logger.Error("处理状态变化事件失败: " + ex.Message, ex);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _messageAggregator.MessageReceived -= OnMessageReceived;
        _douyinService.Dispose();
        _weixinService.Dispose();
        _messageAggregator.Dispose();
        _lifecycleLock.Dispose();
    }
}
