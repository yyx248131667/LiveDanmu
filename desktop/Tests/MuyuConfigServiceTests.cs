using LiveDanmuDesktop.Models;
using LiveDanmuDesktop.Services;
using Xunit;

namespace LiveDanmuDesktop.Tests;

public sealed class MuyuConfigServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "LiveDanmuDesktop.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_WhenFileDoesNotExist_ReturnsDefaults()
    {
        var service = new MuyuConfigService(Path.Combine(_tempDirectory, "missing.json"));

        var config = service.Load();

        Assert.Equal("好运连连", config.Douyin.Text);
        Assert.Equal(80, config.Weixin.Volume);
        Assert.True(config.Douyin.SoundEnabled);
    }

    [Fact]
    public void SaveThenLoad_PreservesPlatformSettings()
    {
        var path = Path.Combine(_tempDirectory, "muyu.json");
        var service = new MuyuConfigService(path);
        var expected = new MuyuConfig();
        expected.Douyin.Text = "感谢支持";
        expected.Douyin.Volume = 42;
        expected.Douyin.GiftRules["小心心"] = 3;
        expected.Weixin.GreenScreen = true;

        service.Save(expected);
        var actual = service.Load();

        Assert.Equal("感谢支持", actual.Douyin.Text);
        Assert.Equal(42, actual.Douyin.Volume);
        Assert.Equal(3, actual.Douyin.GiftRules["小心心"]);
        Assert.True(actual.Weixin.GreenScreen);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
