using System.Text.Json;
using LiveDanmuDesktop.Models;
using LiveDanmuDesktop.Services;
using Xunit;

namespace LiveDanmuDesktop.Tests;

public sealed class MuyuLikeCountTests
{
    [Fact]
    public void ExtractLikeCount_ReadsDouyinBatchCount()
    {
        var message = new LiveMessage
        {
            ExtraData = new Dictionary<string, JsonElement>
            {
                ["likeCount"] = JsonSerializer.SerializeToElement(3286)
            }
        };

        Assert.Equal(3286, MuyuService.ExtractLikeCount(message));
    }

    [Fact]
    public void ExtractLikeCount_AcceptsStringAndFallsBackToOne()
    {
        var stringMessage = new LiveMessage
        {
            ExtraData = new Dictionary<string, JsonElement>
            {
                ["like_count"] = JsonSerializer.SerializeToElement("42")
            }
        };

        Assert.Equal(42, MuyuService.ExtractLikeCount(stringMessage));
        Assert.Equal(1, MuyuService.ExtractLikeCount(new LiveMessage()));
    }
}
