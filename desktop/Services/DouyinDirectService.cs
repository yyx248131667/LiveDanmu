using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Douyin;
using Google.Protobuf;
using LiveDanmuDesktop.Models;

namespace LiveDanmuDesktop.Services;

public class DouyinDirectService : IDisposable
{
	private readonly MessageAggregator _messageAggregator;

	private readonly Logger _logger;

	private readonly CookieManager _cookieManager;

	private WebSocket? _webSocket;

	private TcpClient? _tcpClient;

	private CancellationTokenSource? _cancellationTokenSource;

	private string? _roomId;

	private string? _wsUrl;

	private string? _ttwid;
	private string? _actualRoomId;
	private string? _pushId;
	private string _fetchCursor = string.Empty;
	private string _fetchInternalExt = string.Empty;

	private bool _disposed;

	private Timer? _heartbeatTimer;

	private const int MaxReconnectAttempts = 10;

	private const int BaseReconnectDelayMs = 2000;

	private const int MaxReconnectDelayMs = 60000;

	private int _reconnectAttempts = 0;

	private bool _shouldReconnect = false;

	private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

	public bool IsRunning { get; private set; }

	public event EventHandler<string>? StatusChanged;

	public DouyinDirectService(MessageAggregator messageAggregator, Logger logger, CookieManager cookieManager)
	{
		_messageAggregator = messageAggregator ?? throw new ArgumentNullException("messageAggregator");
		_logger = logger ?? throw new ArgumentNullException("logger");
		_cookieManager = cookieManager ?? throw new ArgumentNullException("cookieManager");
	}

	public async Task StartAsync(string roomId)
	{
		if (IsRunning)
		{
			_logger.Warn("抖音直连服务已在运行中");
			return;
		}
		_roomId = roomId;
		_cancellationTokenSource = new CancellationTokenSource();
		_shouldReconnect = true;
		_reconnectAttempts = 0;
		try
		{
			_logger.Info("启动抖音直连服务，房间ID: " + roomId);
			this.StatusChanged?.Invoke(this, "正在连接...");
			_wsUrl = await GetWebSocketUrlAsync(roomId);
			_logger.Info("WebSocket URL: " + _wsUrl.Substring(0, Math.Min(100, _wsUrl.Length)) + "...");
			await ConnectWebSocketAsync(_wsUrl, _cancellationTokenSource.Token);
			IsRunning = true;
			_reconnectAttempts = 0;
			this.StatusChanged?.Invoke(this, "已连接");
			_logger.Info("抖音直连服务启动成功");
			StartHeartbeat();
			_ = Task.Run(() => ReceiveWithReconnectAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
			_ = Task.Run(() => PollMessagesAsync(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			_logger.Error("启动抖音直连服务失败: " + ex2.Message, ex2);
			this.StatusChanged?.Invoke(this, "连接失败: " + ex2.Message);
			await StopAsync();
			throw;
		}
	}

	private async Task<string> GetWebSocketUrlAsync(string roomId)
	{
		try
		{
			using HttpClient httpClient = new HttpClient();
			httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
			httpClient.DefaultRequestHeaders.Add("Referer", "https://live.douyin.com/" + roomId);
			httpClient.DefaultRequestHeaders.Add("Origin", "https://live.douyin.com");
			httpClient.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
			httpClient.DefaultRequestHeaders.Add("Accept-Language", "zh-CN,zh;q=0.9");
			string? cookieString = _cookieManager.GetDouyinCookie(forceReload: true);
			if (!string.IsNullOrWhiteSpace(cookieString))
			{
				httpClient.DefaultRequestHeaders.Add("Cookie", cookieString);
				_logger.Info($"[Douyin] 已加载 Cookie ({cookieString.Length} 字符)");
				Match ttwidMatch = Regex.Match(cookieString, "ttwid=([^;]+)");
				if (ttwidMatch.Success)
				{
					_ttwid = ttwidMatch.Groups[1].Value;
					_logger.Info("[Douyin] 从 Cookie 提取 ttwid: " + _ttwid.Substring(0, Math.Min(20, _ttwid.Length)) + "...");
				}
			}
			else
			{
				_logger.Warn("[Douyin] 未找到 Cookie，将以游客身份连接");
			}
			if (string.IsNullOrEmpty(_ttwid))
			{
				await FetchTtwidAsync(httpClient);
			}
			string url = "https://live.douyin.com/webcast/room/web/enter/?aid=6383&room_id=" + roomId + "&web_rid=" + roomId;
			string jsonStr = await (await httpClient.GetAsync(url)).Content.ReadAsStringAsync();
			_logger.Debug("直播间信息响应: " + jsonStr.Substring(0, Math.Min(200, jsonStr.Length)) + "...");
			if (string.IsNullOrWhiteSpace(jsonStr))
			{
				throw new Exception("抖音直播间信息返回为空，请检查 Cookie 是否有效");
			}
			jsonStr = jsonStr.Trim();
			if (!jsonStr.StartsWith("{") && !jsonStr.StartsWith("["))
			{
				_logger.Error("[Douyin] 直播间信息返回非 JSON: " + jsonStr.Substring(0, Math.Min(100, jsonStr.Length)));
				throw new Exception("抖音返回非 JSON 数据（可能需要登录或Cookie已过期）");
			}
			using JsonDocument doc = JsonDocument.Parse(jsonStr);
			JsonElement root = doc.RootElement;
			string? actualRoomId = null;
			if (root.TryGetProperty("data", out var data) && data.TryGetProperty("data", out var innerData))
			{
				JsonElement idStr2;
				JsonElement idElement2;
				if (innerData.ValueKind == JsonValueKind.Array)
				{
					foreach (JsonElement item in innerData.EnumerateArray())
					{
						if (item.TryGetProperty("id_str", out var idStr))
						{
							actualRoomId = idStr.GetString();
							break;
						}
						if (item.TryGetProperty("id", out var idElement))
						{
							actualRoomId = ((idElement.ValueKind == JsonValueKind.Number) ? idElement.GetInt64().ToString() : idElement.GetString());
							break;
						}
						idStr = default(JsonElement);
						idElement = default(JsonElement);
					}
				}
				else if (innerData.TryGetProperty("id_str", out idStr2))
				{
					actualRoomId = idStr2.GetString();
				}
				else if (innerData.TryGetProperty("id", out idElement2))
				{
					actualRoomId = ((idElement2.ValueKind == JsonValueKind.Number) ? idElement2.GetInt64().ToString() : idElement2.GetString());
				}
			}
			if (string.IsNullOrEmpty(actualRoomId))
			{
				_logger.Warn("无法从响应中获取实际房间ID，使用输入的: " + roomId);
				actualRoomId = roomId;
			}
			_logger.Info("实际房间ID: " + actualRoomId);
			string pushId = FindStringProperty(root, "user_unique_id") ?? GeneratePushId();
			_actualRoomId = actualRoomId;
			_pushId = pushId;
			_logger.Info("[Douyin] user_unique_id: " + MaskIdentifier(pushId));
			long fetchTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			(string cursor, string internalExt) = await FetchStateAsync(httpClient, actualRoomId, pushId, cookieString, string.Empty, string.Empty, true);
			_fetchCursor = cursor;
			_fetchInternalExt = internalExt;
			if (string.IsNullOrWhiteSpace(cursor))
			{
				cursor = $"d-1_u-1_fh-0_t-{fetchTime}_r-1";
			}
			if (string.IsNullOrWhiteSpace(internalExt))
			{
				internalExt = $"internal_src:dim|wss_push_room_id:{actualRoomId}|wss_push_did:{pushId}|first_req_ms:{fetchTime}|fetch_time:{fetchTime}|seq:1|wss_info:0-{fetchTime}-0-0";
			}
			string signature = SignatureService.GenerateSignature(actualRoomId, pushId, "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
			return $"wss://webcast100-ws-web-hl.douyin.com/webcast/im/push/v2/?app_name=douyin_web&version_code=180800&webcast_sdk_version=1.0.15&update_version_code=1.0.15&compress=gzip&device_platform=web&cookie_enabled=true&screen_width=1920&screen_height=1080&browser_language=zh-CN&browser_platform=Win32&browser_name=Mozilla&browser_version=5.0%20(Windows%20NT%2010.0;%20Win64;%20x64)%20AppleWebKit/537.36%20(KHTML,%20like%20Gecko)%20Chrome/120.0.0.0%20Safari/537.36&browser_online=true&tz_name=Etc/GMT-8&cursor={Uri.EscapeDataString(cursor)}&internal_ext={Uri.EscapeDataString(internalExt)}&host=https://live.douyin.com&aid=6383&live_id=1&did_rule=3&endpoint=live_pc&support_wrds=1&user_unique_id={pushId}&im_path=/webcast/im/fetch/&identity=audience&need_persist_msg_count=15&insert_task_id=&live_reason=&room_id={actualRoomId}&heartbeatDuration=0&signature={signature}";
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			_logger.Error("获取WebSocket URL失败: " + ex2.Message, ex2);
			throw;
		}
	}

	private async Task FetchTtwidAsync(HttpClient httpClient)
	{
		try
		{
			if ((await httpClient.GetAsync("https://live.douyin.com/")).Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies))
			{
				foreach (string cookie in cookies)
				{
					if (cookie.StartsWith("ttwid="))
					{
						Match match = Regex.Match(cookie, "ttwid=([^;]+)");
						if (match.Success)
						{
							_ttwid = match.Groups[1].Value;
							_logger.Info("[Douyin] 获取到 ttwid: " + _ttwid.Substring(0, Math.Min(20, _ttwid.Length)) + "...");
							return;
						}
					}
				}
			}
			_logger.Warn("[Douyin] 未能获取 ttwid");
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			_logger.Warn("[Douyin] 获取 ttwid 失败: " + ex2.Message);
		}
	}

	private static string GeneratePushId()
	{
		Random random = new Random();
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append(random.Next(1, 10));
		for (int i = 0; i < 18; i++)
		{
			stringBuilder.Append(random.Next(0, 10));
		}
		return stringBuilder.ToString();
	}

	private async Task ConnectWebSocketAsync(string wsUrl, CancellationToken cancellationToken)
	{
		Uri uri = new Uri(wsUrl);
		string host = uri.Host;
		int port = ((uri.Port > 0) ? uri.Port : 443);
		string pathAndQuery = uri.PathAndQuery;
		_logger.Info($"[Douyin] 正在连接 WebSocket: {host}:{port} (Raw TCP + 手动握手)...");
		TcpClient tcpClient = new TcpClient();
		await tcpClient.ConnectAsync(host, port, cancellationToken);
		SslStream sslStream = new SslStream(tcpClient.GetStream(), leaveInnerStreamOpen: false, (object sender, X509Certificate? cert, X509Chain? chain, SslPolicyErrors errors) => true);
		SslClientAuthenticationOptions sslOptions = new SslClientAuthenticationOptions
		{
			TargetHost = host,
			ApplicationProtocols = null
		};
		await sslStream.AuthenticateAsClientAsync(sslOptions, cancellationToken);
		string secWebSocketKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
		string? cookieString = _cookieManager.GetDouyinCookie(forceReload: true);
		string upgradeRequest = string.Concat(str1: (!string.IsNullOrWhiteSpace(cookieString)) ? ("Cookie: " + cookieString + "\r\n") : ((!string.IsNullOrEmpty(_ttwid)) ? ("Cookie: ttwid=" + _ttwid + "\r\n") : ""), str0: $"GET {pathAndQuery} HTTP/1.1\r\nHost: {host}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: {secWebSocketKey}\r\nSec-WebSocket-Version: 13\r\nOrigin: https://live.douyin.com\r\nReferer: https://live.douyin.com/{_roomId}\r\nPragma: no-cache\r\nCache-Control: no-cache\r\nUser-Agent: {UserAgent}\r\n", str2: "\r\n");
		byte[] requestBytes = Encoding.ASCII.GetBytes(upgradeRequest);
		await sslStream.WriteAsync(requestBytes, cancellationToken);
		await sslStream.FlushAsync(cancellationToken);
		byte[] responseBuffer = new byte[4096];
		StringBuilder responseBuilder = new StringBuilder();
		int totalRead = 0;
		while (!responseBuilder.ToString().Contains("\r\n\r\n"))
		{
			int bytesRead = await sslStream.ReadAsync(responseBuffer.AsMemory(0, responseBuffer.Length), cancellationToken);
			if (bytesRead == 0)
			{
				throw new Exception("服务器关闭了连接");
			}
			responseBuilder.Append(Encoding.ASCII.GetString(responseBuffer, 0, bytesRead));
			totalRead += bytesRead;
		}
		string responseText = responseBuilder.ToString();
		string statusLine = responseText.Split('\r', '\n')[0];
		_logger.Info("[Douyin] 握手响应: " + statusLine);
		if (!statusLine.Contains("101"))
		{
			_logger.Error("[Douyin] 握手失败，完整响应:\n" + responseText.Substring(0, Math.Min(500, responseText.Length)));
			tcpClient.Dispose();
			if (responseText.Contains("Handshake-Msg: DEVICE_BLOCKED", StringComparison.OrdinalIgnoreCase))
			{
				throw new WebSocketException("抖音拒绝了设备签名，请重新登录后再连接");
			}
			throw new WebSocketException("WebSocket 升级失败: " + statusLine);
		}
		_webSocket?.Dispose();
		_webSocket = WebSocket.CreateFromStream(sslStream, new WebSocketCreationOptions
		{
			IsServer = false,
			KeepAliveInterval = TimeSpan.FromSeconds(30L)
		});
		_tcpClient = tcpClient;
		_logger.Info("[Douyin] WebSocket 连接成功: " + host + " ✅");
	}

	private async Task ReceiveWithReconnectAsync(CancellationToken cancellationToken)
	{
		while (_shouldReconnect && !cancellationToken.IsCancellationRequested)
		{
			try
			{
				await ReceiveLoopAsync(cancellationToken);
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch (Exception ex2)
			{
				if (!_shouldReconnect || cancellationToken.IsCancellationRequested)
				{
					break;
				}
				_reconnectAttempts++;
				if (_reconnectAttempts > 10)
				{
					_logger.Error($"[Douyin] 已达最大重连次数 ({10})，停止服务");
					this.StatusChanged?.Invoke(this, "重连失败，已停止");
					IsRunning = false;
					break;
				}
				int delay = Math.Min(60000, 2000 * (int)Math.Pow(2.0, _reconnectAttempts - 1));
				_logger.Warn($"[Douyin] 连接断开: {ex2.Message}，{delay / 1000}秒后第{_reconnectAttempts}次重连");
				this.StatusChanged?.Invoke(this, $"连接断开，{delay / 1000}秒后重连 ({_reconnectAttempts}/{10})");
				try
				{
					await Task.Delay(delay, cancellationToken);
					_cookieManager.InvalidateCache();
					var roomId = _roomId ?? throw new InvalidOperationException("重连时房间 ID 为空");
					_wsUrl = await GetWebSocketUrlAsync(roomId);
					await ConnectWebSocketAsync(_wsUrl, cancellationToken);
					_reconnectAttempts = 0;
					this.StatusChanged?.Invoke(this, "已重新连接");
					_logger.Info("[Douyin] 重连成功");
				}
				catch (OperationCanceledException)
				{
					break;
				}
				catch (Exception ex4)
				{
					_logger.Error("[Douyin] 重连失败: " + ex4.Message);
				}
			}
		}
	}

	private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[65536];
		while (!cancellationToken.IsCancellationRequested)
		{
			WebSocket? webSocket = _webSocket;
			if (webSocket == null || webSocket.State != WebSocketState.Open)
			{
				break;
			}
			try
			{
				WebSocketReceiveResult result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
				if (result.MessageType == WebSocketMessageType.Binary)
				{
					await ParseProtobufMessageAsync(buffer, result.Count);
					continue;
				}
				if (result.MessageType == WebSocketMessageType.Close)
				{
					_logger.Info("WebSocket 连接已关闭");
					break;
				}
				continue;
			}
			catch (OperationCanceledException)
			{
			}
			catch (WebSocketException ex2)
			{
				WebSocketException ex3 = ex2;
				_logger.Error("WebSocket 异常: " + ex3.Message);
				throw;
			}
			catch (Exception ex4)
			{
				Exception ex5 = ex4;
				_logger.Error("接收消息失败: " + ex5.Message, ex5);
				throw;
			}
			break;
		}
		WebSocket? webSocket2 = _webSocket;
		if ((webSocket2 == null || webSocket2.State != WebSocketState.Open) && _shouldReconnect)
		{
			throw new Exception($"WebSocket 状态异常: {_webSocket?.State}");
		}
	}

	private async Task ParseProtobufMessageAsync(byte[] data, int length)
	{
		try
		{
			PushFrame pushFrame = PushFrame.Parser.ParseFrom(data, 0, length);
			_logger.Debug($"收到PushFrame: LogId={pushFrame.LogId}, PayloadType={pushFrame.PayloadType}");
			if (pushFrame.PayloadType != "msg")
			{
				return;
			}
			byte[] payload = pushFrame.Payload.ToByteArray();
			bool isGzip = false;
			foreach (HeadersList header in pushFrame.HeadersList)
			{
				if (header.Key == "compress_type" && header.Value == "gzip")
				{
					isGzip = true;
					break;
				}
			}
			if (isGzip)
			{
				payload = DecompressGzip(payload);
			}
			else if (payload.Length >= 2 && payload[0] == 31 && payload[1] == 139)
			{
				try
				{
					payload = DecompressGzip(payload);
				}
				catch
				{
				}
			}
			Response response = Response.Parser.ParseFrom(payload);
			_logger.Debug($"收到Response: MessagesList={response.MessagesList.Count}, NeedAck={response.NeedAck}");
			if (response.NeedAck)
			{
				await SendAckAsync(pushFrame.LogId, response.InternalExt);
			}
			foreach (Message message in response.MessagesList)
			{
				await ProcessMessageAsync(message);
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			_logger.Error("解析Protobuf失败: " + ex2.Message, ex2);
		}
	}

	private async Task SendAckAsync(ulong logId, string internalExt)
	{
		try
		{
			PushFrame ackFrame = new PushFrame
			{
				LogId = logId,
				PayloadType = "ack",
				Payload = ByteString.CopyFromUtf8(internalExt)
			};
			byte[] ackData = ackFrame.ToByteArray();
			WebSocket? webSocket = _webSocket;
			if (webSocket != null && webSocket.State == WebSocketState.Open)
			{
				await webSocket.SendAsync(new ArraySegment<byte>(ackData), WebSocketMessageType.Binary, endOfMessage: true, CancellationToken.None);
				_logger.Debug($"ACK 已发送: LogId={logId}");
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			_logger.Warn("发送ACK失败: " + ex2.Message);
		}
	}

	private byte[] DecompressGzip(byte[] data)
	{
		using MemoryStream stream = new MemoryStream(data);
		using GZipStream gZipStream = new GZipStream(stream, CompressionMode.Decompress);
		using MemoryStream memoryStream = new MemoryStream();
		gZipStream.CopyTo(memoryStream);
		return memoryStream.ToArray();
	}

	private async Task ProcessMessageAsync(Message message)
	{
		try
		{
			_logger.Debug("处理消息: Method=" + message.Method);
			switch (message.Method)
			{
			case "WebcastChatMessage":
				ProcessChatMessage(message.Payload.ToByteArray());
				break;
			case "WebcastGiftMessage":
				ProcessGiftMessage(message.Payload.ToByteArray());
				break;
			case "WebcastLikeMessage":
				ProcessLikeMessage(message.Payload.ToByteArray());
				break;
			case "WebcastMemberMessage":
				ProcessMemberMessage(message.Payload.ToByteArray());
				break;
			case "WebcastSocialMessage":
				ProcessSocialMessage(message.Payload.ToByteArray());
				break;
			case "WebcastRoomUserSeqMessage":
				ProcessRoomUserSeqMessage(message.Payload.ToByteArray());
				break;
			case "WebcastRoomStatsMessage":
				ProcessRoomStatsMessage(message.Payload.ToByteArray());
				break;
			case "WebcastControlMessage":
				ProcessControlMessage(message.Payload.ToByteArray());
				break;
			default:
				_logger.Debug("未处理的消息类型: " + message.Method);
				break;
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			_logger.Error("处理消息失败: " + ex2.Message, ex2);
		}
		await Task.CompletedTask;
	}

	private void ProcessChatMessage(byte[] payload)
	{
		try
		{
			ChatMessage chatMessage = ChatMessage.Parser.ParseFrom(payload);
			string text = chatMessage.User?.NickName ?? "未知用户";
			string text2 = chatMessage.Content ?? "";
			LiveMessage message = new LiveMessage
			{
				Platform = "douyin",
				MsgType = "chat",
				Username = text,
				Content = text2,
				Timestamp = DateTime.Now
			};
			_messageAggregator.PublishMessage(message);
			_logger.Debug("[Douyin] [chat] " + text + ": " + text2);
		}
		catch (Exception ex)
		{
			_logger.Error("处理聊天消息失败: " + ex.Message, ex);
		}
	}

	private void ProcessGiftMessage(byte[] payload)
	{
		try
		{
			GiftMessage giftMessage = GiftMessage.Parser.ParseFrom(payload);
			string text = giftMessage.User?.NickName ?? "未知用户";
			string value = giftMessage.Gift?.Name ?? "礼物";
			string value2 = ((giftMessage.GiftId != 0) ? giftMessage.GiftId : (giftMessage.Gift?.Id ?? 0)).ToString();
			ulong repeatCount = giftMessage.RepeatCount;
			string json = JsonSerializer.Serialize(new Dictionary<string, object>
			{
				["giftId"] = value2,
				["giftName"] = value,
				["giftCount"] = ((repeatCount != 0) ? repeatCount : 1),
				["repeatCount"] = ((repeatCount != 0) ? repeatCount : 1)
			});
			Dictionary<string, JsonElement> extraData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new();
			LiveMessage message = new LiveMessage
			{
				Platform = "douyin",
				MsgType = "gift",
				Username = text,
				Content = $"{value} x{repeatCount}",
				Timestamp = DateTime.Now,
				ExtraData = extraData
			};
			_messageAggregator.PublishMessage(message);
			_logger.Debug($"[Douyin] [gift] {text}: {value}(id={value2}) x{repeatCount}");
		}
		catch (Exception ex)
		{
			_logger.Error("处理礼物消息失败: " + ex.Message, ex);
		}
	}

	private void ProcessLikeMessage(byte[] payload)
	{
		try
		{
			LikeMessage likeMessage = LikeMessage.Parser.ParseFrom(payload);
			string text = likeMessage.User?.NickName ?? "未知用户";
			ulong count = likeMessage.Count;
			string json = JsonSerializer.Serialize(new Dictionary<string, object> { ["likeCount"] = ((count != 0) ? count : 1) });
			Dictionary<string, JsonElement> extraData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new();
			LiveMessage message = new LiveMessage
			{
				Platform = "douyin",
				MsgType = "like",
				Username = text,
				Content = $"点赞 x{count}",
				Timestamp = DateTime.Now,
				ExtraData = extraData
			};
			_messageAggregator.PublishMessage(message);
			_logger.Debug($"[Douyin] [like] {text}: x{count}");
		}
		catch (Exception ex)
		{
			_logger.Error("处理点赞消息失败: " + ex.Message, ex);
		}
	}

	private void ProcessMemberMessage(byte[] payload)
	{
		try
		{
			MemberMessage memberMessage = MemberMessage.Parser.ParseFrom(payload);
			string text = memberMessage.User?.NickName ?? "未知用户";
			LiveMessage message = new LiveMessage
			{
				Platform = "douyin",
				MsgType = "member",
				Username = text,
				Content = "进入直播间",
				Timestamp = DateTime.Now
			};
			_messageAggregator.PublishMessage(message);
			_logger.Debug("[Douyin] [member] " + text + " 进入直播间");
		}
		catch (Exception ex)
		{
			_logger.Error("处理进入直播间消息失败: " + ex.Message, ex);
		}
	}

	private void ProcessSocialMessage(byte[] payload)
	{
		try
		{
			SocialMessage socialMessage = SocialMessage.Parser.ParseFrom(payload);
			string text = socialMessage.User?.NickName ?? "未知用户";
			LiveMessage message = new LiveMessage
			{
				Platform = "douyin",
				MsgType = "social",
				Username = text,
				Content = "关注了主播",
				Timestamp = DateTime.Now
			};
			_messageAggregator.PublishMessage(message);
			_logger.Debug("[Douyin] [social] " + text + " 关注了主播");
		}
		catch (Exception ex)
		{
			_logger.Error("处理关注消息失败: " + ex.Message, ex);
		}
	}

	private void ProcessRoomUserSeqMessage(byte[] payload)
	{
		try
		{
			RoomUserSeqMessage roomUserSeqMessage = RoomUserSeqMessage.Parser.ParseFrom(payload);
			long totalUser = roomUserSeqMessage.TotalUser;
			string json = JsonSerializer.Serialize(new Dictionary<string, object> { ["viewer_count"] = totalUser });
			Dictionary<string, JsonElement> extraData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new();
			LiveMessage message = new LiveMessage
			{
				Platform = "douyin",
				MsgType = "viewer_count",
				Username = "系统",
				Content = $"当前观众: {totalUser}",
				Timestamp = DateTime.Now,
				ExtraData = extraData
			};
			_messageAggregator.PublishMessage(message);
			_logger.Debug($"[Douyin] [viewer_count] {totalUser}");
		}
		catch (Exception ex)
		{
			_logger.Error("处理观众数消息失败: " + ex.Message, ex);
		}
	}

	private async Task<(string Cursor, string InternalExt)> FetchStateAsync(HttpClient httpClient, string roomId, string pushId, string? cookieString, string cursor, string internalExt, bool isInitial)
	{
		try
		{
			string msToken = ExtractCookie(cookieString, "msToken") ?? string.Empty;
			var parameters = new Dictionary<string, string>
			{
				["resp_content_type"] = "protobuf", ["did_rule"] = "3", ["device_id"] = "",
				["app_name"] = "douyin_web", ["endpoint"] = "live_pc", ["support_wrds"] = "1",
				["user_unique_id"] = pushId, ["identity"] = "audience", ["need_persist_msg_count"] = "15",
				["insert_task_id"] = "", ["live_reason"] = "", ["room_id"] = roomId,
				["version_code"] = "180800", ["last_rtt"] = "0", ["live_id"] = "1", ["aid"] = "6383",
				["fetch_rule"] = "1", ["cursor"] = cursor, ["internal_ext"] = internalExt, ["device_platform"] = "web",
				["cookie_enabled"] = "true", ["screen_width"] = "1920", ["screen_height"] = "1080",
				["browser_language"] = "zh-CN", ["browser_platform"] = "Win32", ["browser_name"] = "Mozilla",
				["browser_version"] = "5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
				["browser_online"] = "true", ["tz_name"] = "Etc/GMT-8", ["msToken"] = msToken
			};
			string query = string.Join("&", parameters.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
			using HttpResponseMessage httpResponse = await httpClient.GetAsync("https://live.douyin.com/webcast/im/fetch/?" + query);
			byte[] payload = await httpResponse.Content.ReadAsByteArrayAsync();
			httpResponse.EnsureSuccessStatusCode();
			Douyin.Response response = Douyin.Response.Parser.ParseFrom(payload);
			_logger.Info($"[Douyin] 初始消息拉取成功: messages={response.MessagesList.Count}, cursor={response.Cursor.Length}, internal_ext={response.InternalExt.Length}");
			foreach (Message message in response.MessagesList)
			{
				await ProcessMessageAsync(message);
			}
			return (response.Cursor, response.InternalExt);
		}
		catch (Exception ex)
		{
			_logger.Warn("[Douyin] 初始消息拉取失败，将使用兼容参数继续连接: " + ex.Message);
			return (string.Empty, string.Empty);
		}
	}

	private async Task PollMessagesAsync(CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(_actualRoomId) || string.IsNullOrWhiteSpace(_pushId)) return;
		using HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
		client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
		client.DefaultRequestHeaders.Add("Referer", "https://live.douyin.com/" + _roomId);
		client.DefaultRequestHeaders.Add("Origin", "https://live.douyin.com");
		client.DefaultRequestHeaders.Add("Accept", "*/*");
		client.DefaultRequestHeaders.Add("Accept-Language", "zh-CN,zh;q=0.9");
		string? cookies = _cookieManager.GetDouyinCookie(forceReload: true);
		if (!string.IsNullOrWhiteSpace(cookies)) client.DefaultRequestHeaders.Add("Cookie", cookies);
		_logger.Info("[Douyin] HTTP incremental message fallback started");
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				await Task.Delay(1200, cancellationToken);
				(string cursor, string internalExt) = await FetchStateAsync(client, _actualRoomId, _pushId, cookies, _fetchCursor, _fetchInternalExt, false);
				if (!string.IsNullOrWhiteSpace(cursor)) _fetchCursor = cursor;
				if (!string.IsNullOrWhiteSpace(internalExt)) _fetchInternalExt = internalExt;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception ex)
			{
				_logger.Warn("[Douyin] HTTP incremental loop error: " + ex.Message);
				await Task.Delay(3000, cancellationToken);
			}
		}
	}

	private static string? ExtractCookie(string? cookies, string name)
	{
		if (string.IsNullOrWhiteSpace(cookies)) return null;
		Match match = Regex.Match(cookies, $@"(?:^|;\s*){Regex.Escape(name)}=([^;]+)", RegexOptions.IgnoreCase);
		return match.Success ? match.Groups[1].Value : null;
	}

	private static string? FindStringProperty(JsonElement element, string propertyName)
	{
		if (element.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty property in element.EnumerateObject())
			{
				if (property.NameEquals(propertyName) && (property.Value.ValueKind == JsonValueKind.String || property.Value.ValueKind == JsonValueKind.Number))
					return property.Value.ToString();
				string? nested = FindStringProperty(property.Value, propertyName);
				if (!string.IsNullOrWhiteSpace(nested)) return nested;
			}
		}
		else if (element.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in element.EnumerateArray())
			{
				string? nested = FindStringProperty(item, propertyName);
				if (!string.IsNullOrWhiteSpace(nested)) return nested;
			}
		}
		return null;
	}

	private static string MaskIdentifier(string value) => value.Length <= 8 ? value : value[..4] + "..." + value[^4..];

	private void ProcessRoomStatsMessage(byte[] payload)
	{
		try
		{
			RoomStatsMessage stats = RoomStatsMessage.Parser.ParseFrom(payload);
			long count = stats.DisplayValue > 0 ? stats.DisplayValue : stats.Total;
			var extraData = new Dictionary<string, JsonElement>
			{
				["viewer_count"] = JsonSerializer.SerializeToElement(count),
				["display_long"] = JsonSerializer.SerializeToElement(stats.DisplayLong ?? string.Empty)
			};
			_messageAggregator.PublishMessage(new LiveMessage
			{
				Platform = "douyin",
				MsgType = "viewer_count",
				Username = "系统",
				Content = string.IsNullOrWhiteSpace(stats.DisplayLong) ? $"当前在线: {count}" : stats.DisplayLong,
				Timestamp = DateTime.Now,
				ExtraData = extraData
			});
			_logger.Debug($"[Douyin] [room_stats] viewers={count}, display={stats.DisplayLong}");
		}
		catch (Exception ex)
		{
			_logger.Error("处理房间统计消息失败: " + ex.Message, ex);
		}
	}

	private void ProcessControlMessage(byte[] payload)
	{
		try
		{
			ControlMessage controlMessage = ControlMessage.Parser.ParseFrom(payload);
			int status = controlMessage.Status;
			_logger.Info($"[Douyin] [control] Status={status}");
			if (status == 3)
			{
				LiveMessage message = new LiveMessage
				{
					Platform = "douyin",
					MsgType = "control",
					Username = "系统",
					Content = "直播已结束",
					Timestamp = DateTime.Now
				};
				_messageAggregator.PublishMessage(message);
				_shouldReconnect = false;
				this.StatusChanged?.Invoke(this, "直播已结束");
			}
		}
		catch (Exception ex)
		{
			_logger.Error("处理控制消息失败: " + ex.Message, ex);
		}
	}

	private void StartHeartbeat()
	{
		_heartbeatTimer?.Dispose();
		_heartbeatTimer = new Timer(async delegate
		{
			try
			{
				await SendHeartbeatAsync();
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				_logger.Error("发送心跳失败: " + ex2.Message, ex2);
			}
		}, null, TimeSpan.FromSeconds(10L), TimeSpan.FromSeconds(10L));
	}

	private async Task SendHeartbeatAsync()
	{
		WebSocket? webSocket = _webSocket;
		if (webSocket == null || webSocket.State != WebSocketState.Open)
		{
			return;
		}
		try
		{
			PushFrame pushFrame = new PushFrame
			{
				LogId = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
				PayloadType = "hb"
			};
			byte[] data = pushFrame.ToByteArray();
			await webSocket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, endOfMessage: true, CancellationToken.None);
			_logger.Debug("心跳已发送");
		}
		catch (Exception ex)
		{
			_logger.Error("发送心跳失败: " + ex.Message, ex);
		}
	}

	public async Task StopAsync()
	{
		_logger.Info("停止抖音直连服务...");
		IsRunning = false;
		_shouldReconnect = false;
		try
		{
			_heartbeatTimer?.Dispose();
			_heartbeatTimer = null;
			_cancellationTokenSource?.Cancel();
			_cancellationTokenSource?.Dispose();
			_cancellationTokenSource = null;
			if (_webSocket != null)
			{
				if (_webSocket.State == WebSocketState.Open)
				{
					try
					{
						await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "关闭连接", CancellationToken.None);
					}
					catch
					{
					}
				}
				_webSocket.Dispose();
				_webSocket = null;
			}
			if (_tcpClient != null)
			{
				try
				{
					_tcpClient.Close();
				}
				catch
				{
				}
				_tcpClient.Dispose();
				_tcpClient = null;
			}
			this.StatusChanged?.Invoke(this, "已断开");
			_logger.Info("抖音直连服务已停止");
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			_logger.Error("停止服务失败: " + ex2.Message, ex2);
		}
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			_shouldReconnect = false;
			StopAsync().Wait();
		}
	}
}
