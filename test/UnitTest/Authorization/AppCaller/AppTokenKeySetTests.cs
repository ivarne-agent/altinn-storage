using System;
using Altinn.Platform.Storage.Authorization.AppCaller;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.Authorization.AppCaller;

public class AppTokenKeySetTests
{
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-30T12:00:00Z"));

    [Fact]
    public void TryGetKey_BeforeFirstLoad_ReturnsFalse()
    {
        AppTokenKeySet keySet = new(_time);

        Assert.False(keySet.TryGetKey("ttd", "v1", out _));
        Assert.Null(keySet.KeySetAge);
        Assert.Equal(0, keySet.Count);
    }

    [Fact]
    public void TryGetKey_FindsKeyByOrgAndVersion()
    {
        SecurityKey ttdV1 = AppTokenTestHelper.CreateEcKey("v1");
        SecurityKey ttdV2 = AppTokenTestHelper.CreateEcKey("v2");
        SecurityKey skdV3 = AppTokenTestHelper.CreateEcKey("v3");
        AppTokenKeySet keySet = new(_time);

        keySet.Replace([new("ttd", "v1", ttdV1), new("ttd", "v2", ttdV2), new("skd", "v3", skdV3)]);

        Assert.True(keySet.TryGetKey("ttd", "v2", out SecurityKey? found));
        Assert.Same(ttdV2, found);
        Assert.True(keySet.TryGetKey("TTD", "v1", out _));
        Assert.False(keySet.TryGetKey("ttd", "v3", out _));
        Assert.False(keySet.TryGetKey("ttd", "V1", out _));
        Assert.False(keySet.TryGetKey("nav", "v1", out _));
        Assert.Equal(3, keySet.Count);
    }

    [Fact]
    public void Replace_SwapsWholeSet()
    {
        AppTokenKeySet keySet = new(_time);
        keySet.Replace([new("ttd", "v1", AppTokenTestHelper.CreateEcKey("v1"))]);

        keySet.Replace([new("ttd", "v2", AppTokenTestHelper.CreateEcKey("v2"))]);

        Assert.False(keySet.TryGetKey("ttd", "v1", out _));
        Assert.True(keySet.TryGetKey("ttd", "v2", out _));
        Assert.Equal(1, keySet.Count);
    }

    [Fact]
    public void KeySetAge_GrowsUntilNextReplace()
    {
        AppTokenKeySet keySet = new(_time);
        keySet.Replace([]);

        _time.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(TimeSpan.FromMinutes(4), keySet.KeySetAge);

        keySet.Replace([]);
        Assert.Equal(TimeSpan.Zero, keySet.KeySetAge);
    }
}
