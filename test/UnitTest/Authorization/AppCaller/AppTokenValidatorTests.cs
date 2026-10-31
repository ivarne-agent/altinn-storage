using System;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization.AppCaller;
using Altinn.Platform.Storage.Configuration;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.Authorization.AppCaller;

public class AppTokenValidatorTests
{
    private const string TtdApp = "ttd/test-app";

    private readonly ECDsaSecurityKey _ttdKey = AppTokenTestHelper.CreateEcKey("ttd-v1");
    private readonly RsaSecurityKey _ttdRsaKey = AppTokenTestHelper.CreateRsaKey("ttd-v2");
    private readonly ECDsaSecurityKey _skdKey = AppTokenTestHelper.CreateEcKey("skd-v1");
    private readonly AppTokenValidator _validator;

    public AppTokenValidatorTests()
    {
        AppTokenKeySet keySet = new(TimeProvider.System);
        keySet.Replace([
            new AppTokenSigningKey("ttd", "ttd-v1", AppTokenTestHelper.PublicKey(_ttdKey)),
            new AppTokenSigningKey("ttd", "ttd-v2", AppTokenTestHelper.PublicKey(_ttdRsaKey)),
            new AppTokenSigningKey("skd", "skd-v1", AppTokenTestHelper.PublicKey(_skdKey)),
        ]);
        _validator = new AppTokenValidator(
            keySet,
            AppTokenTestHelper.Monitor(new AppCallerSettings())
        );
    }

    [Fact]
    public async Task ValidateAsync_ValidEs256Token_ReturnsValid()
    {
        string token = AppTokenTestHelper.CreateToken(_ttdKey);

        AppCallerValidationResult result = await Validate(token);

        Assert.Equal(AppCallerOutcome.Valid, result.Outcome);
        Assert.Equal(AppCallerReasons.Ok, result.Reason);
        Assert.Equal("ttd", result.Org);
        Assert.Equal(TtdApp, result.AppId);
        Assert.Equal("ttd-v1", result.Kid);
    }

    [Fact]
    public async Task ValidateAsync_ValidRs256Token_ReturnsValid()
    {
        string token = AppTokenTestHelper.CreateToken(_ttdRsaKey);

        AppCallerValidationResult result = await Validate(token);

        Assert.Equal(AppCallerOutcome.Valid, result.Outcome);
        Assert.Equal("ttd-v2", result.Kid);
    }

    [Fact]
    public async Task ValidateAsync_AppIdDiffersOnlyInCase_ReturnsValid()
    {
        string token = AppTokenTestHelper.CreateToken(_ttdKey);

        AppCallerValidationResult result = await Validate(token, "TTD/Test-App");

        Assert.Equal(AppCallerOutcome.Valid, result.Outcome);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task ValidateAsync_NoToken_ReturnsMissing(string? token)
    {
        AppCallerValidationResult result = await Validate(token);

        Assert.Equal(AppCallerOutcome.Missing, result.Outcome);
        Assert.Equal(AppCallerReasons.Missing, result.Reason);
    }

    [Theory]
    [InlineData("not-a-token")]
    [InlineData("a.b.c")]
    [InlineData("eyJhbGciOiJFUzI1NiJ9.e30")]
    public async Task ValidateAsync_MalformedToken_ReturnsMalformed(string token)
    {
        AppCallerValidationResult result = await Validate(token);

        Assert.Equal(AppCallerOutcome.Invalid, result.Outcome);
        Assert.Equal(AppCallerReasons.Malformed, result.Reason);
    }

    [Fact]
    public async Task ValidateAsync_Expired_ReturnsExpired()
    {
        DateTime now = DateTime.UtcNow;
        string token = AppTokenTestHelper.CreateToken(
            _ttdKey,
            notBefore: now.AddHours(-3),
            expires: now.AddSeconds(-60)
        );

        AppCallerValidationResult result = await Validate(token);

        Assert.Equal(AppCallerOutcome.Invalid, result.Outcome);
        Assert.Equal(AppCallerReasons.Expired, result.Reason);
        Assert.Equal("ttd", result.Org);
    }

    [Fact]
    public async Task ValidateAsync_ExpiredWithinSkew_ReturnsValid()
    {
        DateTime now = DateTime.UtcNow;
        string token = AppTokenTestHelper.CreateToken(
            _ttdKey,
            notBefore: now.AddHours(-2),
            expires: now.AddSeconds(-10)
        );

        AppCallerValidationResult result = await Validate(token);

        Assert.Equal(AppCallerOutcome.Valid, result.Outcome);
    }

    [Fact]
    public async Task ValidateAsync_NotYetValidWithinSkew_ReturnsValid()
    {
        string token = AppTokenTestHelper.CreateToken(
            _ttdKey,
            notBefore: DateTime.UtcNow.AddSeconds(10)
        );

        AppCallerValidationResult result = await Validate(token);

        Assert.Equal(AppCallerOutcome.Valid, result.Outcome);
    }

    [Fact]
    public async Task ValidateAsync_NotYetValidBeyondSkew_ReturnsNotYetValid()
    {
        string token = AppTokenTestHelper.CreateToken(
            _ttdKey,
            notBefore: DateTime.UtcNow.AddSeconds(120)
        );

        AppCallerValidationResult result = await Validate(token);

        Assert.Equal(AppCallerOutcome.Invalid, result.Outcome);
        Assert.Equal(AppCallerReasons.NotYetValid, result.Reason);
    }

    [Fact]
    public async Task ValidateAsync_NoExpiry_ReturnsBadLifetime()
    {
        string token = AppTokenTestHelper.CreateToken(_ttdKey, includeExpiry: false);

        AppCallerValidationResult result = await Validate(token);

        Assert.Equal(AppCallerOutcome.Invalid, result.Outcome);
        Assert.Equal(AppCallerReasons.BadLifetime, result.Reason);
    }

    [Fact]
    public async Task ValidateAsync_WrongAudience_ReturnsWrongAudience()
    {
        string token = AppTokenTestHelper.CreateToken(_ttdKey, audience: "altinn-events");

        AppCallerValidationResult result = await Validate(token);

        Assert.Equal(AppCallerOutcome.Invalid, result.Outcome);
        Assert.Equal(AppCallerReasons.WrongAudience, result.Reason);
    }

    [Fact]
    public async Task ValidateAsync_WrongIssuer_ReturnsWrongIssuer()
    {
        string token = AppTokenTestHelper.CreateToken(_ttdKey, issuer: "someone-else");

        AppCallerValidationResult result = await Validate(token);

        Assert.Equal(AppCallerOutcome.Invalid, result.Outcome);
        Assert.Equal(AppCallerReasons.WrongIssuer, result.Reason);
    }

    [Fact]
    public async Task ValidateAsync_UnknownKid_ReturnsUnknownKid()
    {
        string token = AppTokenTestHelper.CreateToken(AppTokenTestHelper.CreateEcKey("ttd-v9"));

        AppCallerValidationResult result = await Validate(token);

        Assert.Equal(AppCallerOutcome.Invalid, result.Outcome);
        Assert.Equal(AppCallerReasons.UnknownKid, result.Reason);
        Assert.Null(result.Org);
        Assert.Equal("ttd-v9", result.Kid);
    }

    [Fact]
    public async Task ValidateAsync_SignedWithKeyOfAnotherOrg_ReturnsUnknownKid()
    {
        // skd's key, but the token claims to be from ttd.
        string token = AppTokenTestHelper.CreateToken(_skdKey, org: "ttd");

        AppCallerValidationResult result = await Validate(token);

        Assert.Equal(AppCallerOutcome.Invalid, result.Outcome);
        Assert.Equal(AppCallerReasons.UnknownKid, result.Reason);
    }

    [Fact]
    public async Task ValidateAsync_KidOfOrgButSignedByAnotherKey_ReturnsBadSignature()
    {
        // A key that claims ttd's kid but is not ttd's key.
        string token = AppTokenTestHelper.CreateToken(AppTokenTestHelper.CreateEcKey("ttd-v1"));

        AppCallerValidationResult result = await Validate(token);

        Assert.Equal(AppCallerOutcome.Invalid, result.Outcome);
        Assert.Equal(AppCallerReasons.BadSignature, result.Reason);
    }

    [Fact]
    public async Task ValidateAsync_AppClaimOfAnotherOrg_ReturnsBadAppClaim()
    {
        string token = AppTokenTestHelper.CreateToken(_ttdKey, org: "ttd", app: "skd/test-app");

        AppCallerValidationResult result = await Validate(token, "skd/test-app");

        Assert.Equal(AppCallerOutcome.Invalid, result.Outcome);
        Assert.Equal(AppCallerReasons.BadAppClaim, result.Reason);
    }

    [Fact]
    public async Task ValidateAsync_TokenForAnotherApp_ReturnsWrongApp()
    {
        string token = AppTokenTestHelper.CreateToken(_ttdKey, app: "ttd/other-app");

        AppCallerValidationResult result = await Validate(token);

        Assert.Equal(AppCallerOutcome.WrongApp, result.Outcome);
        Assert.Equal(AppCallerReasons.WrongApp, result.Reason);
        Assert.Equal("ttd", result.Org);
        Assert.Equal("ttd/other-app", result.AppId);
    }

    [Fact]
    public async Task ValidateAsync_AlgNone_ReturnsBadAlgorithm()
    {
        string token = AppTokenTestHelper.CreateUnsignedToken("ttd-v1");

        AppCallerValidationResult result = await Validate(token);

        Assert.Equal(AppCallerOutcome.Invalid, result.Outcome);
        Assert.Equal(AppCallerReasons.BadAlgorithm, result.Reason);
    }

    [Fact]
    public async Task ValidateAsync_Hs256_ReturnsBadAlgorithm()
    {
        SymmetricSecurityKey hmacKey = new(new byte[32]) { KeyId = "ttd-v1" };
        string token = AppTokenTestHelper.CreateToken(
            hmacKey,
            algorithm: SecurityAlgorithms.HmacSha256
        );

        AppCallerValidationResult result = await Validate(token);

        Assert.Equal(AppCallerOutcome.Invalid, result.Outcome);
        Assert.Equal(AppCallerReasons.BadAlgorithm, result.Reason);
    }

    [Fact]
    public async Task ValidateAsync_TamperedPayload_ReturnsBadSignature()
    {
        string token = AppTokenTestHelper.CreateToken(_ttdKey);
        string otherPayload = AppTokenTestHelper
            .CreateToken(_ttdKey, app: "ttd/other-app")
            .Split('.')[1];
        string[] parts = token.Split('.');
        string tampered = $"{parts[0]}.{otherPayload}.{parts[2]}";

        AppCallerValidationResult result = await Validate(tampered, "ttd/other-app");

        Assert.Equal(AppCallerOutcome.Invalid, result.Outcome);
        Assert.Equal(AppCallerReasons.BadSignature, result.Reason);
    }

    private Task<AppCallerValidationResult> Validate(
        string? token,
        string expectedAppId = TtdApp
    ) => _validator.ValidateAsync(token, expectedAppId, CancellationToken.None);
}
