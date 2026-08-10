# 直播弹幕助手 Lite

一个基于 Avalonia 和 WebView2 的 Windows 抖音直播弹幕查看器。当前轻量版专注于直播间连接、实时弹幕、礼物、点赞、进场消息、在线人数和消息导出。

## 环境要求

- Windows 10/11
- .NET 10 SDK
- Microsoft Edge WebView2 Runtime

## 开发运行

```powershell
dotnet restore LiveDanmuDesktop.csproj
dotnet run --project LiveDanmuDesktop.csproj
```

在左侧输入完整抖音直播间地址，例如 `https://live.douyin.com/123456`，然后点击“连接直播间”。如直播间要求登录，可使用界面中的“登录抖音”。

## 构建

```powershell
dotnet build LiveDanmuDesktop.csproj -c Release
```

## 项目结构

- `Services/DouyinDirectService.cs`：抖音直播连接与消息解析
- `Services/LiveServiceManager.cs`：直播服务生命周期
- `MainWindow.axaml.cs`：桌面窗口与 WebView2 消息桥接
- `wwwroot/`：轻量控制台界面

## 说明

旧版本包含视频号、木鱼和抽奖等实验模块。相关后端源码暂时保留以便迁移，但轻量版主界面不再暴露这些入口。
