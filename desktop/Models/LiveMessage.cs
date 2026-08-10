using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiveDanmuDesktop.Models;

public class LiveMessage
{
	[JsonPropertyName("platform")]
	public string Platform { get; set; } = "";

	[JsonPropertyName("msg_type")]
	public string MsgType { get; set; } = "";

	[JsonIgnore]
	public string Type
	{
		get
		{
			return MsgType;
		}
		set
		{
			MsgType = value;
		}
	}

	[JsonPropertyName("user_id")]
	public string UserId { get; set; } = "";

	[JsonPropertyName("username")]
	public string Username { get; set; } = "";

	[JsonPropertyName("content")]
	public string Content { get; set; } = "";

	[JsonPropertyName("timestamp")]
	public double TimestampUnix { get; set; }

	[JsonIgnore]
	public DateTime Timestamp
	{
		get
		{
			return (TimestampUnix > 0.0) ? DateTimeOffset.FromUnixTimeSeconds((long)TimestampUnix).LocalDateTime : DateTime.Now;
		}
		set
		{
			TimestampUnix = new DateTimeOffset(value).ToUnixTimeSeconds();
		}
	}

	[JsonPropertyName("extra_data")]
	public Dictionary<string, JsonElement>? ExtraData { get; set; }

	[JsonIgnore]
	public string AvatarUrl
	{
		get
		{
			if (ExtraData != null)
			{
				if (ExtraData.TryGetValue("avatar_url", out var value))
				{
					return value.GetString() ?? "";
				}
				if (ExtraData.TryGetValue("avatar", out var value2))
				{
					return value2.GetString() ?? "";
				}
			}
			return "";
		}
	}

	[JsonIgnore]
	public string Method
	{
		get
		{
			if (ExtraData != null && ExtraData.TryGetValue("method", out var value))
			{
				return value.GetString() ?? MapMsgTypeToMethod(MsgType);
			}
			return MapMsgTypeToMethod(MsgType);
		}
	}

	private static string MapMsgTypeToMethod(string msgType)
	{
		if (1 == 0)
		{
		}
		string result = msgType switch
		{
			"chat" => "WebcastChatMessage", 
			"gift" => "WebcastGiftMessage", 
			"like" => "WebcastLikeMessage", 
			"member" => "WebcastMemberMessage", 
			"enter" => "WebcastMemberMessage", 
			"social" => "WebcastSocialMessage", 
			_ => msgType, 
		};
		if (1 == 0)
		{
		}
		return result;
	}
}
