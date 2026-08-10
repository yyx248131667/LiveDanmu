using LiveDanmuDesktop.Services;
using Xunit;

namespace LiveDanmuDesktop.Tests;

public sealed class SignatureServiceTests
{
    [Fact]
    public void GenerateSignature_FindsPublishedServicesFolder()
    {
        var signature = SignatureService.GenerateSignature(
            "123456789",
            "987654321",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120 Safari/537.36");

        Assert.False(string.IsNullOrWhiteSpace(signature));
        Assert.True(signature.Length >= 16);
    }

    [Fact]
    public void EmbeddedEngine_GeneratesDouyinSignature()
    {
        var script = Path.Combine(AppContext.BaseDirectory, "Services", "webmssdk.js");
        var stub = SignatureService.GetXMSStub("123456789", "987654321");

        var signature = SignatureService.ExecuteWithJint(
            stub,
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120 Safari/537.36",
            script);

        Assert.False(string.IsNullOrWhiteSpace(signature));
        Assert.True(signature.Length >= 16);
    }
}
