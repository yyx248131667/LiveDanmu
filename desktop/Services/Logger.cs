using System;
using System.IO;

namespace LiveDanmuDesktop.Services;

public class Logger
{
	private readonly string _logFilePath;

	private readonly object _lock = new object();

	public Logger(string? logFilePath = null)
	{
		_logFilePath = logFilePath ?? Path.Combine(AppPaths.AppDataRoot, "logs", $"app_{DateTime.Now:yyyyMMdd}.log");
		string? directoryName = Path.GetDirectoryName(_logFilePath);
		if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
	}

	public void Info(string message)
	{
		Log("INFO", message);
	}

	public void Warn(string message)
	{
		Log("WARN", message);
	}

	public void Error(string message, Exception? ex = null)
	{
		Log("ERROR", message, ex);
	}

	public void Debug(string message)
	{
		Log("DEBUG", message);
	}

	private void Log(string level, string message, Exception? ex = null)
	{
		lock (_lock)
		{
			try
			{
				string text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
				if (ex != null)
				{
					text += $"\n{ex}";
				}
				File.AppendAllText(_logFilePath, text + Environment.NewLine);
				Console.WriteLine(text);
			}
			catch
			{
			}
		}
	}
}
