using System;
using System.IO;
using System.Text.Json;

namespace LiveDanmuDesktop.Services;

public class ConfigService
{
	private readonly string _configPath;

	public ConfigService(string? configPath = null)
	{
		_configPath = configPath ?? AppPaths.GetDataPath("config.json");
	}

	public LiveConfig Load()
	{
		try
		{
			if (File.Exists(_configPath))
			{
				string json = File.ReadAllText(_configPath);
				return JsonSerializer.Deserialize<LiveConfig>(json) ?? new LiveConfig();
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("加载配置失败: " + ex.Message);
		}
		return new LiveConfig();
	}

	public void Save(LiveConfig config)
	{
		try
		{
			string contents = JsonSerializer.Serialize(config, new JsonSerializerOptions
			{
				WriteIndented = true
			});
			File.WriteAllText(_configPath, contents);
		}
		catch (Exception ex)
		{
			Console.WriteLine("保存配置失败: " + ex.Message);
		}
	}
}
