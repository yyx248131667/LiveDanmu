namespace LiveDanmuDesktop.Services;

public class LiveConfig
{
	public bool EnableDouyin { get; set; } = true;

	public string DouyinRoomId { get; set; } = "975816634199";

	public bool EnableWeixin { get; set; } = true;

	public string WeixinRoomId { get; set; } = "";

	public bool WeixinHeadless { get; set; } = false;
}
