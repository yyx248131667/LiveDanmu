using System;
using Avalonia;
using Avalonia.Logging;
using System.Threading;

namespace LiveDanmuDesktop;

internal class Program
{
	private static Mutex? _singleInstanceMutex;

	[STAThread]
	public static void Main(string[] args)
	{
		_singleInstanceMutex = new Mutex(true, "Local\\LiveDanmuDesktop.SingleInstance", out var isFirstInstance);
		if (!isFirstInstance)
		{
			return;
		}
		try
		{
			BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
		}
		finally
		{
			_singleInstanceMutex.ReleaseMutex();
			_singleInstanceMutex.Dispose();
		}
	}

	public static AppBuilder BuildAvaloniaApp()
	{
		return AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace(LogEventLevel.Warning);
	}
}
