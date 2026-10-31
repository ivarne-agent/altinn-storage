using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Altinn.Common.AccessToken.Services;
using Altinn.Common.PEP.Interfaces;
using Altinn.Platform.Storage.Authorization.AppCaller;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Controllers;
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
using Microsoft.IdentityModel.Tokens;
using Moq;
using Newtonsoft.Json;
using Wolverine;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.Authorization.AppCaller;

/// <summary>
/// Runs the app caller token check through the full pipeline in Enforce mode,
/// using the read status endpoint of an instance owned by tdd/endring-av-navn.
/// </summary>
public class AppCallerTokenIntegrationTests(TestApplicationFactory<InstancesController> factory)
    : IClassFixture<TestApplicationFactory<InstancesController>>
{
    private const string RequestUri =
        "storage/api/v1/instances/1337/824e8304-ad9e-4d79-ac75-bcfa7213223b/readstatus?status=read";

    private readonly ECDsaSecurityKey _tddKey = AppTokenTestHelper.CreateEcKey("tdd-v1");

    [Fact]
    public async Task Enforce_ValidTokenForInstanceApp_ReturnsOk()
    {
        string appToken = AppTokenTestHelper.CreateToken(
            _tddKey,
            org: "tdd",
            app: "tdd/endring-av-navn"
        );

        HttpResponseMessage response = await Send(appToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Enforce_MissingToken_Returns401ProblemDetails()
    {
        HttpResponseMessage response = await Send(appToken: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        ProblemDetails? problem = JsonConvert.DeserializeObject<ProblemDetails>(
            await response.Content.ReadAsStringAsync()
        );
        Assert.Equal(401, problem?.Status);
    }

    [Fact]
    public async Task Enforce_TokenForAnotherApp_Returns403()
    {
        string appToken = AppTokenTestHelper.CreateToken(_tddKey, org: "tdd", app: "tdd/other");

        HttpResponseMessage response = await Send(appToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<HttpResponseMessage> Send(string? appToken)
    {
        HttpClient client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            PrincipalUtil.GetToken(3, 1337, 2)
        );
        HttpRequestMessage request = new(HttpMethod.Put, RequestUri);
        if (appToken is not null)
        {
            request.Headers.Add(RequireAppCallerTokenAttribute.HeaderName, appToken);
        }

        return await client.SendAsync(request);
    }

    private HttpClient CreateClient()
    {
        AppTokenKeySet keySet = new(TimeProvider.System);
        keySet.Replace([
            new AppTokenSigningKey("tdd", "tdd-v1", AppTokenTestHelper.PublicKey(_tddKey)),
        ]);

        return factory
            .WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services =>
                {
                    services.AddMockRepositories();
                    services.AddSingleton(new Mock<IKeyVaultClientWrapper>().Object);
                    services.AddSingleton<
                        IPartiesWithInstancesClient,
                        PartiesWithInstancesClientMock
                    >();
                    services.AddSingleton<IPDP, PepWithPDPAuthorizationMockSI>();
                    services.AddSingleton<
                        IPostConfigureOptions<JwtCookieOptions>,
                        JwtCookiePostConfigureOptionsStub
                    >();
                    services.AddSingleton<
                        IPublicSigningKeyProvider,
                        PublicSigningKeyProviderMock
                    >();
                    services.AddSingleton(new Mock<IMessageBus>().Object);
                    services.Configure<AppCallerSettings>(s => s.Mode = AppCallerMode.Enforce);
                    services.AddSingleton<IAppTokenSigningKeyProvider>(keySet);
                })
            )
            .CreateClient();
    }
}
