using System.Text.Json;
using LiveDanmuDesktop.Models;
using Xunit;

namespace LiveDanmuDesktop.Tests;

public sealed class MuyuConfigPropertyTests
{
    [Fact]
    public void Defaults_AreIndependentAcrossPlatforms()
    {
        var config = new MuyuConfig();

        config.Douyin.GiftRules["礼物"] = 5;

        Assert.False(config.Weixin.GiftRules.ContainsKey("礼物"));
        Assert.NotSame(config.Douyin, config.Weixin);
        Assert.NotSame(config.Douyin.GiftRules, config.Weixin.GiftRules);
    }

    [Fact]
    public void JsonOptions_UseCamelCaseAndRoundTrip()
    {
        var expected = new MuyuConfig();
        expected.Douyin.AudioSpeed = 125;
        expected.Weixin.CustomSkinData = "data:image/png;base64,test";

        var json = JsonSerializer.Serialize(expected, MuyuConfig.JsonOptions);
        var actual = JsonSerializer.Deserialize<MuyuConfig>(json, MuyuConfig.JsonOptions);

        Assert.Contains("\"audioSpeed\"", json);
        Assert.DoesNotContain("\"AudioSpeed\"", json);
        Assert.NotNull(actual);
        Assert.Equal(125, actual.Douyin.AudioSpeed);
        Assert.Equal("data:image/png;base64,test", actual.Weixin.CustomSkinData);
    }
}
