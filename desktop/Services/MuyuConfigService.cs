using System;
using System.IO;
using System.Text.Json;
using LiveDanmuDesktop.Models;

namespace LiveDanmuDesktop.Services;

public class MuyuConfigService
{
	private readonly string _configPath;

	public MuyuConfigService()
	{
		_configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LiveDanmuDesktop", "muyu_config.json");
	}

	internal MuyuConfigService(string configPath)
	{
		_configPath = configPath;
	}

	public MuyuConfig Load()
	{
		if (!File.Exists(_configPath))
		{
			return new MuyuConfig();
		}
		try
		{
			string json = File.ReadAllText(_configPath);
			MuyuConfig? muyuConfig = JsonSerializer.Deserialize<MuyuConfig>(json, MuyuConfig.JsonOptions);
			return muyuConfig ?? new MuyuConfig();
		}
		catch (Exception ex) when (((ex is JsonException || ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			Console.Error.WriteLine("[MuyuConfigService] Failed to load config: " + ex.Message);
			return new MuyuConfig();
		}
	}

	public void Save(MuyuConfig config)
	{
		string? directoryName = Path.GetDirectoryName(_configPath);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		string contents = JsonSerializer.Serialize(config, MuyuConfig.JsonOptions);
		File.WriteAllText(_configPath, contents);
	}
}
