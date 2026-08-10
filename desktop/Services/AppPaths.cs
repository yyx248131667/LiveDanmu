using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace LiveDanmuDesktop.Services;

public static class AppPaths
{
	private static readonly Dictionary<string, CoreWebView2Environment> CachedEnvironments = new(StringComparer.OrdinalIgnoreCase);

	private static readonly SemaphoreSlim _envLock;

	public static string AppDataRoot { get; }

	public static string RuntimeRoot { get; }

	public static string WebView2UserDataFolder
	{
		get
		{
			string text = Path.Combine(AppDataRoot, "WebView2Data");
			if (!Directory.Exists(text))
			{
				Directory.CreateDirectory(text);
			}
			return text;
		}
	}

	static AppPaths()
	{
		_envLock = new SemaphoreSlim(1, 1);
		string? processPath = Environment.ProcessPath;
		string executableRoot = !string.IsNullOrEmpty(processPath)
			? Path.GetDirectoryName(processPath) ?? AppDomain.CurrentDomain.BaseDirectory
			: AppDomain.CurrentDomain.BaseDirectory;
		AppDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiveDanmuDesktop");
		Directory.CreateDirectory(AppDataRoot);
		RuntimeRoot = AppContext.BaseDirectory;
		MigrateLegacyData(executableRoot);
		Console.WriteLine("[AppPaths] AppDataRoot (持久化目录): " + AppDataRoot);
		Console.WriteLine("[AppPaths] RuntimeRoot (运行时目录): " + RuntimeRoot);
	}

	private static void MigrateLegacyData(string legacyRoot)
	{
		if (string.Equals(legacyRoot, AppDataRoot, StringComparison.OrdinalIgnoreCase)) return;
		foreach (string fileName in new[] { "cookie_config.yaml", "weixin_cookie_config.yaml", "muyu_config.json" })
		{
			string source = Path.Combine(legacyRoot, fileName);
			string destination = Path.Combine(AppDataRoot, fileName);
			if (File.Exists(source) && !File.Exists(destination))
			{
				try { File.Copy(source, destination); }
				catch (Exception ex) { Console.WriteLine($"[AppPaths] 迁移 {fileName} 失败: {ex.Message}"); }
			}
		}
	}

	public static string GetDataPath(string fileName)
	{
		return Path.Combine(AppDataRoot, fileName);
	}

	public static string GetResourcePath(string fileName)
	{
		return Path.Combine(RuntimeRoot, fileName);
	}

	public static string? FindFile(string fileName, int maxParentLevels = 4)
	{
		string text = Path.Combine(AppDataRoot, fileName);
		if (File.Exists(text))
		{
			return text;
		}
		if (AppDataRoot != RuntimeRoot)
		{
			string text2 = Path.Combine(RuntimeRoot, fileName);
			if (File.Exists(text2))
			{
				return text2;
			}
		}
		string text3 = Path.Combine(Environment.CurrentDirectory, fileName);
		if (File.Exists(text3))
		{
			return text3;
		}
		DirectoryInfo directoryInfo = new DirectoryInfo(AppDataRoot);
		for (int i = 0; i < maxParentLevels; i++)
		{
			if (directoryInfo?.Parent == null)
			{
				break;
			}
			directoryInfo = directoryInfo.Parent;
			string text4 = Path.Combine(directoryInfo.FullName, fileName);
			if (File.Exists(text4))
			{
				return text4;
			}
			string text5 = Path.Combine(directoryInfo.FullName, "desktop", fileName);
			if (File.Exists(text5))
			{
				return text5;
			}
		}
		return null;
	}

	public static async Task<CoreWebView2Environment> CreateWebView2EnvironmentAsync(string profile = "shared")
	{
		if (CachedEnvironments.TryGetValue(profile, out var cached))
		{
			return cached;
		}
		await _envLock.WaitAsync();
		try
		{
			if (CachedEnvironments.TryGetValue(profile, out cached))
			{
				return cached;
			}
			string safeProfile = string.Concat(profile.Select(ch => char.IsLetterOrDigit(ch) || ch == '-' ? ch : '_'));
			string udf = Path.Combine(WebView2UserDataFolder, safeProfile);
			Directory.CreateDirectory(udf);
			Console.WriteLine("[AppPaths] 创建 WebView2 环境, UserDataFolder: " + udf);
			cached = await CoreWebView2Environment.CreateAsync(null, udf);
			CachedEnvironments[profile] = cached;
			return cached;
		}
		finally
		{
			_envLock.Release();
		}
	}
}
