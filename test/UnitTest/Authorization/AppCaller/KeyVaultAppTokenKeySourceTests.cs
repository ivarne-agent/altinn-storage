using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization.AppCaller;
using Altinn.Platform.Storage.Configuration;
using Azure;
using Azure.Security.KeyVault.Keys;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;
using KeyVaultJsonWebKey = Azure.Security.KeyVault.Keys.JsonWebKey;

namespace Altinn.Platform.Storage.UnitTest.Authorization.AppCaller;

public class KeyVaultAppTokenKeySourceTests
{
    private static readonly Uri _vaultUri = new("https://test.vault.azure.net/");

    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-30T12:00:00Z"));
    private readonly Mock<KeyClient> _client = new();
    private readonly ECDsa _ttdV1 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa _ttdV2 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly RSA _skdV1 = RSA.Create(2048);

    public KeyVaultAppTokenKeySourceTests()
    {
        SetupKeys(
            Key("app-token-signer-ttd"),
            Key("app-token-signer-skd"),
            Key("some-other-key"),
            Key("app-token-signer-")
        );
        SetupVersions(
            "app-token-signer-ttd",
            Version("app-token-signer-ttd", "ttd-v1", enabled: true),
            Version("app-token-signer-ttd", "ttd-v2", enabled: true),
            Version("app-token-signer-ttd", "ttd-disabled", enabled: false),
            Version(
                "app-token-signer-ttd",
                "ttd-expired",
                enabled: true,
                expiresOn: _time.GetUtcNow().AddMinutes(-1)
            )
        );
        SetupVersions(
            "app-token-signer-skd",
            Version("app-token-signer-skd", "skd-v1", enabled: true)
        );
        SetupKey("app-token-signer-ttd", "ttd-v1", new KeyVaultJsonWebKey(_ttdV1, false));
        SetupKey("app-token-signer-ttd", "ttd-v2", new KeyVaultJsonWebKey(_ttdV2, false));
        SetupKey("app-token-signer-skd", "skd-v1", new KeyVaultJsonWebKey(_skdV1, false));
    }

    [Fact]
    public async Task GetKeysAsync_ReturnsEnabledUnexpiredVersionsOfPrefixedKeys()
    {
        KeyVaultAppTokenKeySource source = CreateSource();

        IReadOnlyList<AppTokenSigningKey> keys = await source.GetKeysAsync(CancellationToken.None);

        Assert.Equal(
            ["skd/skd-v1", "ttd/ttd-v1", "ttd/ttd-v2"],
            keys.Select(k => $"{k.Org}/{k.Kid}").Order()
        );
        Assert.All(keys, k => Assert.Equal(k.Kid, k.Key.KeyId));
        Assert.IsType<ECDsaSecurityKey>(keys.Single(k => k.Kid == "ttd-v1").Key);
        Assert.IsType<RsaSecurityKey>(keys.Single(k => k.Kid == "skd-v1").Key);
    }

    [Fact]
    public async Task GetKeysAsync_FetchesEachVersionOnlyOnce()
    {
        KeyVaultAppTokenKeySource source = CreateSource();

        await source.GetKeysAsync(CancellationToken.None);
        await source.GetKeysAsync(CancellationToken.None);

        _client.Verify(
            c => c.GetKeyAsync("app-token-signer-ttd", "ttd-v1", It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task GetKeysAsync_ConvertedKeyVerifiesTokenSignedWithPrivateKey()
    {
        KeyVaultAppTokenKeySource source = CreateSource();
        IReadOnlyList<AppTokenSigningKey> keys = await source.GetKeysAsync(CancellationToken.None);
        AppTokenKeySet keySet = new(TimeProvider.System);
        keySet.Replace(keys);
        AppTokenValidator validator = new(
            keySet,
            AppTokenTestHelper.Monitor(new AppCallerSettings())
        );
        string token = AppTokenTestHelper.CreateToken(
            new ECDsaSecurityKey(_ttdV2) { KeyId = "ttd-v2" }
        );

        AppCallerValidationResult result = await validator.ValidateAsync(
            token,
            "ttd/test-app",
            CancellationToken.None
        );

        Assert.Equal(AppCallerOutcome.Valid, result.Outcome);
    }

    private KeyVaultAppTokenKeySource CreateSource() =>
        new(
            _client.Object,
            Options.Create(new AppCallerSettings()),
            _time,
            NullLogger<KeyVaultAppTokenKeySource>.Instance
        );

    private static KeyProperties Key(string name) =>
        KeyModelFactory.KeyProperties(
            id: new Uri(_vaultUri, $"keys/{name}"),
            vaultUri: _vaultUri,
            name: name
        );

    private static KeyProperties Version(
        string name,
        string version,
        bool enabled,
        DateTimeOffset? expiresOn = null
    )
    {
        KeyProperties properties = KeyModelFactory.KeyProperties(
            id: new Uri(_vaultUri, $"keys/{name}/{version}"),
            vaultUri: _vaultUri,
            name: name,
            version: version
        );
        properties.Enabled = enabled;
        properties.ExpiresOn = expiresOn;
        return properties;
    }

    private void SetupKeys(params KeyProperties[] keys) =>
        _client
            .Setup(c => c.GetPropertiesOfKeysAsync(It.IsAny<CancellationToken>()))
            .Returns(() => Pageable(keys));

    private void SetupVersions(string name, params KeyProperties[] versions) =>
        _client
            .Setup(c => c.GetPropertiesOfKeyVersionsAsync(name, It.IsAny<CancellationToken>()))
            .Returns(() => Pageable(versions));

    private void SetupKey(string name, string version, KeyVaultJsonWebKey key) =>
        _client
            .Setup(c => c.GetKeyAsync(name, version, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
                Response.FromValue(
                    KeyModelFactory.KeyVaultKey(Version(name, version, true), key),
                    Mock.Of<Response>()
                )
            );

    private static AsyncPageable<T> Pageable<T>(IReadOnlyList<T> values)
        where T : notnull =>
        AsyncPageable<T>.FromPages([Page<T>.FromValues(values, null, Mock.Of<Response>())]);
}
