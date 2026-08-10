using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Jint;

namespace LiveDanmuDesktop.Services;

public class SignatureService
{
	private static bool _nodeAvailable = false;

	private static bool _checked = false;

	private static string? _jsPath;

	private static string _nodeExePath = "node";

	public static string GetXMSStub(string roomId, string pushId)
	{
		(string, string)[] source = new(string, string)[13]
		{
			("live_id", "1"),
			("aid", "6383"),
			("version_code", "180800"),
			("webcast_sdk_version", "1.0.15"),
			("room_id", roomId),
			("sub_room_id", ""),
			("sub_channel_id", ""),
			("did_rule", "3"),
			("user_unique_id", pushId),
			("device_platform", "web"),
			("device_type", ""),
			("ac", ""),
			("identity", "audience")
		};
		string s = string.Join(",", source.Select(((string, string) p) => p.Item1 + "=" + p.Item2));
		byte[] inArray = MD5.HashData(Encoding.UTF8.GetBytes(s));
		return Convert.ToHexStringLower(inArray);
	}

	public static string GenerateSignature(string roomId, string pushId, string userAgent)
	{
		string xMSStub = GetXMSStub(roomId, pushId);
		if (!_checked)
		{
			_checked = true;
			_jsPath = FindWebmssdkJs();
		}
		if (_jsPath != null)
		{
			try
			{
				string? signature = ExecuteWithJint(xMSStub, userAgent, _jsPath);
				if (!string.IsNullOrWhiteSpace(signature))
				{
					Console.WriteLine("[Signature] 内置轻量引擎签名成功");
					return signature;
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("[Signature] 内置轻量引擎签名失败: " + ex.Message);
			}
		}
		if (!_nodeAvailable) CheckNodeAvailable();
		if (_nodeAvailable && _jsPath != null)
		{
			try
			{
				string? text = ExecuteWithNode(xMSStub, userAgent, _jsPath);
				if (!string.IsNullOrEmpty(text))
				{
					Console.WriteLine("[Signature] ✅ Node.js 签名: " + text.Substring(0, Math.Min(20, text.Length)) + "...");
					return text;
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("[Signature] ⚠\ufe0f Node.js 签名失败: " + ex.Message);
			}
		}
		Console.WriteLine("[Signature] ⚠\ufe0f 使用 MD5 后备签名（Node.js 不可用）");
		throw new InvalidOperationException(
			"抖音签名组件不可用。请确认 Services/webmssdk.js 完整且系统可运行 Node.js。");
	}

	internal static string? ExecuteWithJint(string xmsStub, string userAgent, string jsPath)
	{
		string code = File.ReadAllText(jsPath, Encoding.UTF8);
		var engine = new Engine(options => options.TimeoutInterval(TimeSpan.FromSeconds(15)));
		engine.SetValue("__userAgent", userAgent);
		engine.SetValue("__stub", xmsStub);
		engine.Execute("var navigator={userAgent:__userAgent};var window=this;var document={};var setTimeout=function(){};");
		engine.Execute(code + "\n;var __signature_result=crawler({'X-MS-STUB':__stub})['X-Bogus'];");
		var result = engine.GetValue("__signature_result");
		return result.IsNull() || result.IsUndefined() ? null : result.AsString();
	}

	private static string? ExecuteWithNode(string xmsStub, string userAgent, string jsPath)
	{
		string contents = $"\nconst fs = require('fs');\nconst vm = require('vm');\nconst code = fs.readFileSync('{jsPath.Replace("\\", "\\\\")}', 'utf8');\nconst ctx = {{\n    navigator: {{ userAgent: '{userAgent}' }},\n    window: {{}},\n    document: {{}},\n    setTimeout: function() {{}},\n}};\nctx.window = ctx;\nctx.window.navigator = ctx.navigator;\nconst sandbox = vm.createContext(ctx);\nvm.runInContext(code, sandbox, {{ timeout: 10000 }});\nif (typeof sandbox.get_sign === 'function') {{\n    const result = sandbox.get_sign('{xmsStub}');\n    process.stdout.write(result || '');\n}} else {{\n    process.stderr.write('get_sign not found');\n}}\n";
		string text = Path.Combine(Path.GetTempPath(), "douyin_sign_wrapper.js");
		File.WriteAllText(text, contents);
		ProcessStartInfo startInfo = new ProcessStartInfo
		{
			FileName = _nodeExePath,
			Arguments = "--stack-size=16384 \"" + text + "\"",
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};
		using Process process = Process.Start(startInfo)
			?? throw new InvalidOperationException("无法启动 Node.js 签名进程");
		string text2 = process.StandardOutput.ReadToEnd();
		string text3 = process.StandardError.ReadToEnd();
		process.WaitForExit(15000);
		if (!string.IsNullOrEmpty(text3))
		{
			Console.WriteLine("[Signature] Node.js stderr: " + text3.Substring(0, Math.Min(200, text3.Length)));
		}
		try
		{
			File.Delete(text);
		}
		catch
		{
		}
		return string.IsNullOrEmpty(text2) ? null : text2.Trim();
	}

	private static void CheckNodeAvailable()
	{
		string[] array = new string[3]
		{
			Path.Combine(AppPaths.AppDataRoot, "node", "node.exe"),
			Path.Combine(AppPaths.RuntimeRoot, "node", "node.exe"),
			Path.Combine(AppPaths.AppDataRoot, "node.exe")
		};
		string[] array2 = array;
		foreach (string path in array2)
		{
			string fullPath = Path.GetFullPath(path);
			if (File.Exists(fullPath))
			{
				_nodeExePath = fullPath;
				Console.WriteLine("[Signature] ✅ 找到内嵌 Node.js: " + fullPath);
				break;
			}
		}
		try
		{
			ProcessStartInfo startInfo = new ProcessStartInfo
			{
				FileName = _nodeExePath,
				Arguments = "--version",
				RedirectStandardOutput = true,
				UseShellExecute = false,
				CreateNoWindow = true
			};
			using Process process = Process.Start(startInfo)
				?? throw new InvalidOperationException("无法启动 Node.js");
			string text = process.StandardOutput.ReadToEnd().Trim();
			process.WaitForExit(5000);
			if (process.ExitCode == 0 && text.StartsWith("v"))
			{
				_nodeAvailable = true;
				Console.WriteLine($"[Signature] ✅ Node.js 可用: {text} ({_nodeExePath})");
			}
		}
		catch
		{
			Console.WriteLine("[Signature] ⚠\ufe0f Node.js 不可用 (尝试路径: " + _nodeExePath + ")");
			_nodeAvailable = false;
		}
	}

	private static string? FindWebmssdkJs()
	{
		string[] paths = new string[7]
		{
			Path.Combine(AppPaths.AppDataRoot, "Services", "webmssdk.js"),
			Path.Combine(AppPaths.AppDataRoot, "webmssdk.js"),
			Path.Combine(AppPaths.RuntimeRoot, "Services", "webmssdk.js"),
			Path.Combine(AppPaths.RuntimeRoot, "webmssdk.js"),
			Path.Combine(AppPaths.RuntimeRoot, "wwwroot", "webmssdk.js"),
			Path.GetFullPath(Path.Combine(AppPaths.AppDataRoot, "..", "..", "..", "Services", "webmssdk.js")),
			Path.GetFullPath(Path.Combine(AppPaths.AppDataRoot, "..", "..", "..", "..", "internal", "platform", "douyin", "webmssdk.js"))
		};
		foreach (string path in paths)
		{
			string fullPath = Path.GetFullPath(path);
			if (File.Exists(fullPath))
			{
				Console.WriteLine("[Signature] 找到 webmssdk.js: " + fullPath);
				return fullPath;
			}
		}
		Console.WriteLine("[Signature] ⚠\ufe0f 找不到 webmssdk.js");
		return null;
	}
}
