using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using LiveDanmuDesktop.Models;
using Microsoft.Win32;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Models;

namespace LiveDanmuDesktop.Services;

public class WeixinSnifferService : IDisposable
{
	private readonly Logger _logger;

	private readonly MessageAggregator _messageAggregator;

	private ProxyServer? _proxyServer;

	private ExplicitProxyEndPoint? _endpoint;

	private HttpListener? _callbackListener;

	private bool _isRunning;

	private int _proxyPort = 18888;

	private int _callbackPort = 18890;

	private bool _originalProxyEnabled;

	private string? _originalProxyServer;

	private int _requestCount = 0;

	private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;

	private const int INTERNET_OPTION_REFRESH = 37;

	public bool IsRunning => _isRunning;

	public event EventHandler<string>? StatusChanged;

	private string GetInjectedScript()
	{
		return $"\n(function() {{\n    if (window.__wx_danmu_hooked__) return;\n    window.__wx_danmu_hooked__ = true;\n    \n    var CALLBACK = 'http://127.0.0.1:{_callbackPort}/danmu';\n    \n    function sendDanmu(data) {{\n        try {{\n            fetch(CALLBACK, {{\n                method: 'POST',\n                headers: {{ 'Content-Type': 'application/json' }},\n                body: JSON.stringify(data),\n                mode: 'no-cors'\n            }}).catch(function(){{}});\n        }} catch(e) {{}}\n    }}\n    \n    // 通知后端已注入成功\n    sendDanmu({{ type: 'inject_success', msg: 'JS注入成功' }});\n    \n    // ========== Hook XMLHttpRequest ==========\n    var origXHROpen = XMLHttpRequest.prototype.open;\n    var origXHRSend = XMLHttpRequest.prototype.send;\n    \n    XMLHttpRequest.prototype.open = function(method, url) {{\n        this._wxUrl = url;\n        this._wxMethod = method;\n        return origXHROpen.apply(this, arguments);\n    }};\n    \n    XMLHttpRequest.prototype.send = function() {{\n        var self = this;\n        var url = self._wxUrl || '';\n        \n        // 监听所有可能包含弹幕的请求\n        if (url.indexOf('mmfinderassistant') >= 0 || \n            url.indexOf('finder') >= 0 ||\n            url.indexOf('live') >= 0) {{\n            \n            self.addEventListener('load', function() {{\n                try {{\n                    if (self.status === 200 && self.responseText) {{\n                        var text = self.responseText;\n                        if (text.length > 10 && (text[0] === '{{' || text[0] === '[')) {{\n                            var json = JSON.parse(text);\n                            // 检查是否包含弹幕相关数据\n                            if (json.data && (json.data.msgList || json.data.appMsgList || json.data.liveInfo)) {{\n                                sendDanmu({{ type: 'api_response', url: url, data: json }});\n                            }}\n                        }}\n                    }}\n                }} catch(e) {{}}\n            }});\n        }}\n        \n        return origXHRSend.apply(this, arguments);\n    }};\n    \n    // ========== Hook fetch ==========\n    var origFetch = window.fetch;\n    window.fetch = function(input, init) {{\n        var url = typeof input === 'string' ? input : (input && input.url ? input.url : '');\n        \n        return origFetch.apply(this, arguments).then(function(response) {{\n            if (url.indexOf('mmfinderassistant') >= 0 || \n                url.indexOf('finder') >= 0 ||\n                url.indexOf('live') >= 0) {{\n                \n                // Clone response to read body without consuming it\n                var cloned = response.clone();\n                cloned.text().then(function(text) {{\n                    try {{\n                        if (text.length > 10 && (text[0] === '{{' || text[0] === '[')) {{\n                            var json = JSON.parse(text);\n                            if (json.data && (json.data.msgList || json.data.appMsgList || json.data.liveInfo)) {{\n                                sendDanmu({{ type: 'api_response', url: url, data: json }});\n                            }}\n                        }}\n                    }} catch(e) {{}}\n                }}).catch(function(){{}});\n            }}\n            return response;\n        }});\n    }};\n    \n    // ========== 轮询 DOM 获取弹幕（备用方案） ==========\n    var lastDanmuTexts = new Set();\n    setInterval(function() {{\n        try {{\n            // 尝试多种可能的弹幕DOM选择器\n            var selectors = [\n                '.comment-item', '.barrage-item', '.live-msg-item',\n                '.danmu-item', '.chat-item', '.msg-item',\n                '[class*=comment]', '[class*=barrage]', '[class*=danmu]',\n                '[class*=chat-msg]', '[class*=live-msg]'\n            ];\n            \n            for (var i = 0; i < selectors.length; i++) {{\n                var items = document.querySelectorAll(selectors[i]);\n                if (items.length > 0) {{\n                    for (var j = Math.max(0, items.length - 10); j < items.length; j++) {{\n                        var text = items[j].textContent.trim();\n                        if (text && !lastDanmuTexts.has(text)) {{\n                            lastDanmuTexts.add(text);\n                            // 尝试提取用户名和内容\n                            var parts = text.split(/[:：]/);\n                            var user = parts.length > 1 ? parts[0].trim() : '未知';\n                            var content = parts.length > 1 ? parts.slice(1).join(':').trim() : text;\n                            sendDanmu({{ type: 'dom_danmu', user: user, content: content }});\n                        }}\n                    }}\n                    // 限制缓存大小\n                    if (lastDanmuTexts.size > 500) {{\n                        var arr = Array.from(lastDanmuTexts);\n                        lastDanmuTexts = new Set(arr.slice(arr.length - 200));\n                    }}\n                    break;\n                }}\n            }}\n        }} catch(e) {{}}\n    }}, 2000);\n    \n    console.log('[LiveDanmu] 弹幕捕获脚本已注入');\n}})();\n";
	}

	public WeixinSnifferService(MessageAggregator messageAggregator, Logger logger)
	{
		_messageAggregator = messageAggregator ?? throw new ArgumentNullException("messageAggregator");
		_logger = logger ?? throw new ArgumentNullException("logger");
	}

	public async Task StartAsync()
	{
		if (_isRunning)
		{
			_logger.Warn("[WeixinSniffer] 嗅探服务已在运行");
			return;
		}
		try
		{
			_logger.Info("[WeixinSniffer] 正在启动视频号弹幕嗅探（代理+JS注入方式）...");
			this.StatusChanged?.Invoke(this, "正在启动...");
			StartCallbackServer();
			_proxyServer = new ProxyServer();
			_proxyServer.CertificateManager.RootCertificateName = "LiveDanmu HTTPS Sniffer";
			_proxyServer.CertificateManager.RootCertificateIssuerName = "LiveDanmu";
			_logger.Info("[WeixinSniffer] 正在生成/加载 CA 根证书...");
			_proxyServer.CertificateManager.EnsureRootCertificate();
			_proxyServer.CertificateManager.TrustRootCertificate(machineTrusted: true);
			_logger.Info("[WeixinSniffer] CA 根证书已就绪");
			_proxyServer.BeforeRequest += OnBeforeRequest;
			_proxyServer.BeforeResponse += OnBeforeResponse;
			_proxyServer.ServerCertificateValidationCallback += delegate(object sender, CertificateValidationEventArgs e)
			{
				e.IsValid = true;
				return Task.CompletedTask;
			};
			bool started = false;
			for (int port = _proxyPort; port < _proxyPort + 10; port++)
			{
				try
				{
					var endpoint = new ExplicitProxyEndPoint(IPAddress.Loopback, port);
					_proxyServer.AddEndPoint(endpoint);
					_proxyServer.Start();
					_endpoint = endpoint;
					_proxyPort = port;
					started = true;
				}
				catch (SocketException)
				{
					_logger.Warn($"[WeixinSniffer] 端口 {port} 被占用，尝试下一个...");
					continue;
				}
				break;
			}
			if (!started)
			{
				throw new InvalidOperationException("无法找到可用端口 (18888-18897)");
			}
			var activeEndpoint = _endpoint
				?? throw new InvalidOperationException("代理端点初始化失败");
			SaveOriginalProxy();
			_proxyServer.SetAsSystemHttpProxy(activeEndpoint);
			_proxyServer.SetAsSystemHttpsProxy(activeEndpoint);
			_isRunning = true;
			_logger.Info($"[WeixinSniffer] ✅ 代理已启动 (端口: {_proxyPort})，回调服务 (端口: {_callbackPort})");
			_logger.Info("[WeixinSniffer] 请在微信 PC 端打开视频号直播间");
			this.StatusChanged?.Invoke(this, $"已启动 (代理:{_proxyPort})");
		}
		catch (Exception ex2)
		{
			Exception ex3 = ex2;
			_logger.Error("[WeixinSniffer] 启动失败: " + ex3.Message, ex3);
			this.StatusChanged?.Invoke(this, "启动失败: " + ex3.Message);
			await StopAsync();
			throw;
		}
	}

	private void StartCallbackServer()
	{
		for (int i = _callbackPort; i < _callbackPort + 10; i++)
		{
			try
			{
				_callbackListener = new HttpListener();
				_callbackListener.Prefixes.Add($"http://127.0.0.1:{i}/");
				_callbackListener.Start();
				_callbackPort = i;
				_logger.Info($"[WeixinSniffer] 回调 HTTP 服务启动在端口 {i}");
				_ = Task.Run(async delegate
				{
					while (_callbackListener?.IsListening ?? false)
					{
						try
						{
							HttpListenerContext context = await _callbackListener.GetContextAsync();
							_ = Task.Run(delegate
							{
								HandleCallbackRequest(context);
							});
						}
						catch (ObjectDisposedException)
						{
							break;
						}
						catch (HttpListenerException)
						{
							break;
						}
						catch (Exception ex3)
						{
							_logger.Error("[WeixinSniffer] 回调请求处理错误: " + ex3.Message);
						}
					}
				});
				return;
			}
			catch
			{
				_callbackListener?.Close();
				_callbackListener = null;
			}
		}
		throw new InvalidOperationException("无法启动回调 HTTP 服务 (18890-18899)");
	}

	private void HandleCallbackRequest(HttpListenerContext context)
	{
		try
		{
			context.Response.AddHeader("Access-Control-Allow-Origin", "*");
			context.Response.AddHeader("Access-Control-Allow-Methods", "POST, OPTIONS");
			context.Response.AddHeader("Access-Control-Allow-Headers", "Content-Type");
			if (context.Request.HttpMethod == "OPTIONS")
			{
				context.Response.StatusCode = 204;
				context.Response.Close();
				return;
			}
			if (context.Request.HttpMethod == "POST" && context.Request.Url?.AbsolutePath == "/danmu")
			{
				using StreamReader streamReader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
				string text = streamReader.ReadToEnd();
				if (!string.IsNullOrEmpty(text))
				{
					ProcessCallbackData(text);
				}
			}
			context.Response.StatusCode = 200;
			context.Response.ContentType = "application/json";
			byte[] bytes = Encoding.UTF8.GetBytes("{}");
			context.Response.OutputStream.Write(bytes, 0, bytes.Length);
			context.Response.Close();
		}
		catch (Exception ex)
		{
			_logger.Error("[WeixinSniffer] 处理回调失败: " + ex.Message);
			try
			{
				context.Response.Close();
			}
			catch
			{
			}
		}
	}

	private void ProcessCallbackData(string json)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(json);
			JsonElement rootElement = jsonDocument.RootElement;
			if (!rootElement.TryGetProperty("type", out var value))
			{
				return;
			}
			switch (value.GetString() ?? "")
			{
			case "inject_success":
				_logger.Info("[WeixinSniffer] ✅ JS 注入成功！弹幕捕获已就绪");
				this.StatusChanged?.Invoke(this, "已连接 - JS注入成功");
				break;
			case "dom_danmu":
			{
				JsonElement value3;
				string text = (rootElement.TryGetProperty("user", out value3) ? (value3.GetString() ?? "未知") : "未知");
				JsonElement value4;
				string text2 = (rootElement.TryGetProperty("content", out value4) ? (value4.GetString() ?? "") : "");
				if (!string.IsNullOrWhiteSpace(text2))
				{
					_messageAggregator.PublishMessage(new LiveMessage
					{
						Platform = "weixin_sniffer",
						MsgType = "chat",
						Username = text,
						Content = text2,
						Timestamp = DateTime.Now
					});
					_logger.Debug("[WeixinSniffer] [DOM弹幕] " + text + ": " + text2);
				}
				break;
			}
			case "api_response":
			{
				if (rootElement.TryGetProperty("data", out var value2))
				{
					ParseApiResponse(value2);
				}
				break;
			}
			}
		}
		catch (Exception ex)
		{
			_logger.Error("[WeixinSniffer] 解析回调数据失败: " + ex.Message);
		}
	}

	private void ParseApiResponse(JsonElement root)
	{
		try
		{
			if (root.TryGetProperty("errCode", out var value) && value.GetInt32() != 0)
			{
				return;
			}
			if (!root.TryGetProperty("data", out var value2))
			{
				value2 = root;
			}
			if (value2.TryGetProperty("liveInfo", out var value3) && value3.TryGetProperty("onlineCnt", out var value4))
			{
				_messageAggregator.PublishMessage(new LiveMessage
				{
					Platform = "weixin_sniffer",
					MsgType = "viewer_count",
					Username = "系统",
					Content = $"观众: {value4.GetInt32()}",
					Timestamp = DateTime.Now
				});
			}
			if (value2.TryGetProperty("msgList", out var value5) && value5.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement item in value5.EnumerateArray())
				{
					ProcessChatMessage(item);
				}
			}
			if (!value2.TryGetProperty("appMsgList", out var value6) || value6.ValueKind != JsonValueKind.Array)
			{
				return;
			}
			foreach (JsonElement item2 in value6.EnumerateArray())
			{
				ProcessAppMessage(item2);
			}
		}
		catch (Exception ex)
		{
			_logger.Error("[WeixinSniffer] 解析API数据失败: " + ex.Message);
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
				string text = (msg.TryGetProperty("nickname", out value2) ? (value2.GetString() ?? "未知") : "未知");
				JsonElement value3;
				string text2 = (msg.TryGetProperty("content", out value3) ? (value3.GetString() ?? "") : "");
				string text3;
				switch (@int)
				{
				default:
					return;
				case 1:
					text3 = "chat";
					break;
				case 10005:
					text3 = "enter";
					text2 = "进入直播间";
					break;
				}
				_messageAggregator.PublishMessage(new LiveMessage
				{
					Platform = "weixin_sniffer",
					MsgType = text3,
					Username = text,
					Content = text2,
					Timestamp = DateTime.Now
				});
				_logger.Debug($"[WeixinSniffer] [{text3}] {text}: {text2}");
			}
		}
		catch (Exception ex)
		{
			_logger.Error("[WeixinSniffer] 处理聊天消息失败: " + ex.Message, ex);
		}
	}

	private void ProcessAppMessage(JsonElement appMsg)
	{
		try
		{
			if (appMsg.TryGetProperty("msgType", out var value))
			{
				int @int = value.GetInt32();
				string text = "未知";
				if (appMsg.TryGetProperty("fromUserContact", out var value2) && value2.TryGetProperty("contact", out var value3) && value3.TryGetProperty("nickname", out var value4))
				{
					text = value4.GetString() ?? "未知";
				}
				string text2;
				string text3;
				switch (@int)
				{
				default:
					return;
				case 20009:
					text2 = "gift";
					text3 = "送出礼物";
					break;
				case 20006:
					text2 = "like";
					text3 = "点赞";
					break;
				case 20013:
					text2 = "gift";
					text3 = "连击礼物";
					break;
				case 20031:
					text2 = "level_up";
					text3 = "等级提升";
					break;
				}
				_messageAggregator.PublishMessage(new LiveMessage
				{
					Platform = "weixin_sniffer",
					MsgType = text2,
					Username = text,
					Content = text3,
					Timestamp = DateTime.Now
				});
				_logger.Debug($"[WeixinSniffer] [{text2}] {text}: {text3}");
			}
		}
		catch (Exception ex)
		{
			_logger.Error("[WeixinSniffer] 处理应用消息失败: " + ex.Message, ex);
		}
	}

	private Task OnBeforeRequest(object sender, SessionEventArgs e)
	{
		_requestCount++;
		if (_requestCount % 500 == 1)
		{
			_logger.Info($"[WeixinSniffer] 代理已处理 {_requestCount} 个请求");
		}
		return Task.CompletedTask;
	}

	private async Task OnBeforeResponse(object sender, SessionEventArgs e)
	{
		string host = e.HttpClient.Request.RequestUri.Host;
		_ = e.HttpClient.Request.RequestUri.AbsoluteUri;
		string contentType = (e.HttpClient.Response.ContentType ?? "").ToLower();
		if (!(host == "channels.weixin.qq.com") || !contentType.Contains("text/html"))
		{
			return;
		}
		try
		{
			string body = await e.GetResponseBodyAsString();
			if (!string.IsNullOrWhiteSpace(body))
			{
				string pathname = e.HttpClient.Request.RequestUri.AbsolutePath;
				_logger.Info("[WeixinSniffer] \ud83c\udfaf 拦截到视频号页面: " + pathname);
				string script = "<script>" + GetInjectedScript() + "</script>";
				body = (body.Contains("<head>") ? body.Replace("<head>", "<head>\n" + script) : ((!body.Contains("<HEAD>")) ? (script + body) : body.Replace("<HEAD>", "<HEAD>\n" + script)));
				e.SetResponseBodyString(body);
				_logger.Info("[WeixinSniffer] ✅ JS 脚本已注入到页面: " + pathname);
				this.StatusChanged?.Invoke(this, "已注入 JS 到 " + pathname);
			}
		}
		catch (Exception ex)
		{
			_logger.Error("[WeixinSniffer] 注入 JS 失败: " + ex.Message);
		}
	}

	public async Task StopAsync()
	{
		if (!_isRunning && _proxyServer == null)
		{
			return;
		}
		try
		{
			if (_callbackListener != null)
			{
				try
				{
					_callbackListener.Stop();
					_callbackListener.Close();
				}
				catch
				{
				}
				_callbackListener = null;
			}
			if (_proxyServer != null)
			{
				_proxyServer.DisableAllSystemProxies();
				RestoreOriginalProxy();
				_proxyServer.BeforeRequest -= OnBeforeRequest;
				_proxyServer.BeforeResponse -= OnBeforeResponse;
				_proxyServer.Stop();
				_proxyServer.Dispose();
				_proxyServer = null;
			}
			_isRunning = false;
			_logger.Info("[WeixinSniffer] ✅ 嗅探服务已停止");
			this.StatusChanged?.Invoke(this, "已停止");
		}
		catch (Exception ex)
		{
			_logger.Error("[WeixinSniffer] 停止失败: " + ex.Message, ex);
			RestoreOriginalProxy();
		}
	}

	private void SaveOriginalProxy()
	{
		try
		{
			using RegistryKey? registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings", writable: false);
			if (registryKey != null)
			{
				_originalProxyEnabled = (int)(registryKey.GetValue("ProxyEnable", 0) ?? ((object)0)) != 0;
				_originalProxyServer = registryKey.GetValue("ProxyServer", null) as string;
			}
			_logger.Info($"[WeixinSniffer] 已保存原始代理: enabled={_originalProxyEnabled}, server={_originalProxyServer}");
		}
		catch (Exception ex)
		{
			_logger.Error("[WeixinSniffer] 保存代理设置失败: " + ex.Message, ex);
		}
	}

	private void RestoreOriginalProxy()
	{
		try
		{
			using RegistryKey? registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings", writable: true);
			if (registryKey != null)
			{
				registryKey.SetValue("ProxyEnable", _originalProxyEnabled ? 1 : 0);
				if (_originalProxyServer != null)
				{
					registryKey.SetValue("ProxyServer", _originalProxyServer);
				}
				else
				{
					registryKey.DeleteValue("ProxyServer", throwOnMissingValue: false);
				}
			}
			RefreshInternetSettings();
			_logger.Info("[WeixinSniffer] 系统代理已恢复");
		}
		catch (Exception ex)
		{
			_logger.Error("[WeixinSniffer] 恢复代理失败: " + ex.Message, ex);
		}
	}

	[DllImport("wininet.dll")]
	private static extern bool InternetSetOption(nint hInternet, int dwOption, nint lpBuffer, int dwBufferLength);

	private static void RefreshInternetSettings()
	{
		InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
		InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
	}

	public void Dispose()
	{
		StopAsync().GetAwaiter().GetResult();
	}
}
