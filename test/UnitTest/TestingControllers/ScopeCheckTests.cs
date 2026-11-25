using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Authorization.ABAC.Xacml.JsonProfile;
using Altinn.Common.AccessToken.Services;
using Altinn.Common.PEP.Interfaces;
using Altinn.Platform.Storage.Authorization.Scopes;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Altinn.Platform.Storage.UnitTest.Mocks;
using Altinn.Platform.Storage.UnitTest.Mocks.Authentication;
using Altinn.Platform.Storage.UnitTest.Mocks.Clients;
using Altinn.Platform.Storage.UnitTest.Mocks.Repository;
using Altinn.Platform.Storage.UnitTest.Utils;
using Altinn.Platform.Storage.Wrappers;
using AltinnCore.Authentication.JwtCookie;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Wolverine;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers;

/// <summary>
/// The API scope check on instance endpoints, through the HTTP pipeline.
/// </summary>
public class ScopeCheckTests : IAsyncLifetime
{
    private const string AppId = "tdd/endring-av-navn";
    private const string InstancePath =
        "storage/api/v1/instances/1337/46133fb5-a9f2-45d4-90b1-f6d93ad40713";
    private const string MissingInstancePath =
        "storage/api/v1/instances/1337/0d1c1c62-6b4b-4d8f-9e4d-2b1c5b0f1a3e";
    private const string AppUserReadScope = "test:[app]/instances.read";
    private const string AppServiceOwnerWriteScope = "test:[org]/[app]/serviceowner.write";

    private readonly TestApplicationFactory<InstancesController> _factory = new();
    private TestTelemetry _telemetry = null!;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Theory]
    [InlineData(InstancePath)]
    [InlineData(MissingInstancePath)]
    public async Task Enforce_WrongScope_Returns403ProblemWithoutAskingThePdp(string path)
    {
        Mock<IPDP> pdp = new(MockBehavior.Strict);
        HttpClient client = GetClient(ScopeCheckMode.Enforce, pdp: pdp);
        SetToken(client, PrincipalUtil.GetToken(3, 1337, 3, scopes: ["altinn:something"]));

        using HttpResponseMessage response = await client.GetAsync(path);

        await AssertInsufficientScope(response);
        pdp.Verify(p => p.GetDecisionForRequest(It.IsAny<XacmlJsonRequestRoot>()), Times.Never);
        await _telemetry.AssertScopeCheckAsync(response, "missing_builtin", "read");
    }

    [Fact]
    public async Task Observe_WrongScope_IsAllowedAndRecorded()
    {
        HttpClient client = GetClient(ScopeCheckMode.Observe);
        SetToken(client, PrincipalUtil.GetToken(3, 1337, 3, scopes: ["altinn:something"]));

        using HttpResponseMessage response = await client.GetAsync(InstancePath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _telemetry.AssertScopeCheckAsync(response, "missing_builtin", "read");
    }

    [Fact]
    public async Task Enforce_BuiltInScope_IsAllowed()
    {
        HttpClient client = GetClient(ScopeCheckMode.Enforce);
        SetToken(client, PrincipalUtil.GetToken(3, 1337, 3, scopes: [ApiScopeRules.InstancesRead]));

        using HttpResponseMessage response = await client.GetAsync(InstancePath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _telemetry.AssertScopeCheckAsync(response, "ok", "read");
    }

    [Fact]
    public async Task Enforce_AppWithApiScopes_RequiresTheAppsUserScope()
    {
        HttpClient client = GetClient(ScopeCheckMode.Enforce, withApiScopes: true);
        SetToken(client, PrincipalUtil.GetToken(3, 1337, 3, scopes: [ApiScopeRules.InstancesRead]));

        using HttpResponseMessage response = await client.GetAsync(InstancePath);

        await AssertInsufficientScope(response);
        await _telemetry.AssertScopeCheckAsync(response, "missing_app", "read");
    }

    [Fact]
    public async Task Enforce_AppWithApiScopes_AcceptsTheAppsUserScope()
    {
        HttpClient client = GetClient(ScopeCheckMode.Enforce, withApiScopes: true);
        SetToken(
            client,
            PrincipalUtil.GetToken(3, 1337, 3, scopes: ["test:endring-av-navn/instances.read"])
        );

        using HttpResponseMessage response = await client.GetAsync(InstancePath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _telemetry.AssertScopeCheckAsync(response, "ok", "read");
    }

    [Fact]
    public async Task Enforce_PdpGivesNoDecisionAfterScopeCheck_Returns503()
    {
        Mock<IPDP> pdp = new();
        pdp.Setup(p => p.GetDecisionForRequest(It.IsAny<XacmlJsonRequestRoot>()))
            .ReturnsAsync((XacmlJsonResponse)null!);
        HttpClient client = GetClient(ScopeCheckMode.Enforce, pdp: pdp);
        SetToken(client, PrincipalUtil.GetToken(3, 1337, 3, scopes: [ApiScopeRules.InstancesRead]));

        using HttpResponseMessage response = await client.GetAsync(InstancePath);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory]
    [InlineData(ScopeCheckMode.Enforce, HttpStatusCode.Forbidden)]
    [InlineData(ScopeCheckMode.Observe, HttpStatusCode.OK)]
    public async Task PartyMismatch_IsRefusedOnlyWhenEnforced(
        ScopeCheckMode mode,
        HttpStatusCode expected
    )
    {
        HttpClient client = GetClient(mode);
        SetToken(client, PrincipalUtil.GetToken(3, 1337, 3, scopes: [ApiScopeRules.InstancesRead]));

        using HttpResponseMessage response = await client.GetAsync(
            "storage/api/v1/instances/1606/46133fb5-a9f2-45d4-90b1-f6d93ad40713"
        );

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Enforce_InstantiationWithoutScope_Returns403ProblemWithoutAskingThePdp()
    {
        Mock<IPDP> pdp = new(MockBehavior.Strict);
        HttpClient client = GetClient(ScopeCheckMode.Enforce, pdp: pdp);
        SetToken(client, PrincipalUtil.GetOrgToken("tdd", scope: ApiScopeRules.InstancesWrite));

        using HttpResponseMessage response = await client.PostAsync(
            $"storage/api/v1/instances?appId={AppId}",
            JsonContent.Create(new Instance { InstanceOwner = new() { PartyId = "1337" } })
        );

        await AssertInsufficientScope(response);
        pdp.Verify(p => p.GetDecisionForRequest(It.IsAny<XacmlJsonRequestRoot>()), Times.Never);
        await _telemetry.AssertScopeCheckAsync(response, "missing_builtin", "write");
    }

    [Fact]
    public async Task Enforce_InstantiationWithTheAppsServiceOwnerScope_IsAllowed()
    {
        HttpClient client = GetClient(ScopeCheckMode.Enforce, withApiScopes: true);
        SetToken(
            client,
            PrincipalUtil.GetOrgToken("tdd", scope: "test:tdd/endring-av-navn/serviceowner.write")
        );

        using HttpResponseMessage response = await client.PostAsync(
            $"storage/api/v1/instances?appId={AppId}",
            JsonContent.Create(new Instance { InstanceOwner = new() { PartyId = "1337" } })
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await _telemetry.AssertScopeCheckAsync(response, "ok_serviceowner_app", "write");
    }

    [Fact]
    public async Task Enforce_MessageboxWithoutSyncAdapterScope_Returns403Problem()
    {
        Mock<IPDP> pdp = new(MockBehavior.Strict);
        HttpClient client = GetClient(ScopeCheckMode.Enforce, pdp: pdp);
        SetToken(client, PrincipalUtil.GetToken(3, 1337, 3, scopes: [ApiScopeRules.PortalEndUser]));

        using HttpResponseMessage response = await client.GetAsync(
            "storage/api/v1/sbl/instances/1337/46133fb5-a9f2-45d4-90b1-f6d93ad40713"
        );

        await AssertInsufficientScope(response);
        pdp.Verify(p => p.GetDecisionForRequest(It.IsAny<XacmlJsonRequestRoot>()), Times.Never);
        await _telemetry.AssertScopeCheckAsync(response, "missing_syncadapter", "read");
    }

    [Fact]
    public async Task Observe_MessageboxWithoutSyncAdapterScope_IsAllowedAndRecorded()
    {
        HttpClient client = GetClient(ScopeCheckMode.Observe);
        SetToken(client, PrincipalUtil.GetToken(3, 1337, 3, scopes: [ApiScopeRules.PortalEndUser]));

        using HttpResponseMessage response = await client.GetAsync(
            "storage/api/v1/sbl/instances/1337/46133fb5-a9f2-45d4-90b1-f6d93ad40713"
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _telemetry.AssertScopeCheckAsync(response, "missing_syncadapter", "read");
    }

    [Theory]
    [InlineData("test:custom/syncadapter", HttpStatusCode.OK)]
    [InlineData(ApiScopeRules.SyncAdapter, HttpStatusCode.Forbidden)]
    public async Task Enforce_Messagebox_RequiresTheConfiguredSyncAdapterScope(
        string tokenScope,
        HttpStatusCode expected
    )
    {
        HttpClient client = GetClient(
            ScopeCheckMode.Enforce,
            syncAdapterScope: "test:custom/syncadapter"
        );
        SetToken(client, PrincipalUtil.GetToken(3, 1337, 3, scopes: [tokenScope]));

        using HttpResponseMessage response = await client.GetAsync(
            "storage/api/v1/sbl/instances/1337/46133fb5-a9f2-45d4-90b1-f6d93ad40713"
        );

        Assert.Equal(expected, response.StatusCode);
    }

    private static void SetToken(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static async Task AssertInsufficientScope(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("insufficient_scope", problem?.Type);
    }

    private HttpClient GetClient(
        ScopeCheckMode mode,
        Mock<IPDP>? pdp = null,
        bool withApiScopes = false,
        string? syncAdapterScope = null
    )
    {
        Mock<IApplicationRepository> applicationRepository = new();
        applicationRepository
            .Setup(r =>
                r.FindOne(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(
                (string appId, string org, CancellationToken _) =>
                    new Application
                    {
                        Id = appId,
                        Org = org,
                        Title = new() { { "nb", "Test" } },
                        DataTypes = [],
                        PartyTypesAllowed = new(),
                        ApiScopes = withApiScopes
                            ? new ApiScopesConfiguration
                            {
                                Users = new ApiScopes { Read = AppUserReadScope },
                                ServiceOwners = new ApiScopes { Write = AppServiceOwnerWriteScope },
                            }
                            : null,
                    }
            );

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddMockRepositories();
                services.AddSingleton(applicationRepository.Object);
                services.AddSingleton(new Mock<IKeyVaultClientWrapper>().Object);
                services.AddSingleton<
                    IPartiesWithInstancesClient,
                    PartiesWithInstancesClientMock
                >();
                services.AddSingleton<IPDP, PepWithPDPAuthorizationMockSI>();
                if (pdp is not null)
                {
                    services.AddSingleton(pdp.Object);
                }

                services.AddSingleton<
                    IPostConfigureOptions<JwtCookieOptions>,
                    JwtCookiePostConfigureOptionsStub
                >();
                services.AddSingleton<IPublicSigningKeyProvider, PublicSigningKeyProviderMock>();
                services.AddSingleton(new Mock<IMessageBus>().Object);
                services.PostConfigure<GeneralSettings>(settings =>
                {
                    settings.InstanceBuiltInScopeCheck = mode;
                    settings.InstanceAppScopeCheck = mode;
                    settings.MessageboxSyncAdapterScopeCheck = mode;
                    if (syncAdapterScope is not null)
                    {
                        settings.InstanceSyncAdapterScope = syncAdapterScope;
                    }
                });
            });
        });

        HttpClient client = factory.CreateClient();
        TestTelemetry.IdentifyRequests(client);
        _telemetry = factory.Services.GetRequiredService<TestTelemetry>();
        return client;
    }
}
