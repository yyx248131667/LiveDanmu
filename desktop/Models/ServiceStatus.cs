namespace LiveDanmuDesktop.Models;

public class ServiceStatus
{
	public string Platform { get; set; } = "";

	public bool IsConnected { get; set; }

	public string Message { get; set; } = "";
}
