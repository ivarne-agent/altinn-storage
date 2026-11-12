using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Common.AccessToken.Services;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Altinn.Platform.Storage.UnitTest.Mocks;
using Altinn.Platform.Storage.UnitTest.Mocks.Authentication;
using Altinn.Platform.Storage.UnitTest.Utils;
using AltinnCore.Authentication.JwtCookie;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
using Wolverine;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers;

public class StorageAccountsControllerTests(
    TestApplicationFactory<StorageAccountsController> factory
) : IClassFixture<TestApplicationFactory<StorageAccountsController>>
{
    private const string _basePath = "storage/api/v1/admin/storageaccounts";

    private static readonly DateTimeOffset _created = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    private static readonly StorageAccountRow _azureRow = new(
        3,
        new AzureBlobProviderInfo("ttdaltinndatastorage", "data"),
        "ttd",
        1,
        true,
        _created
    );

    private static readonly StorageAccountRow _readOnlyRow = new(
        4,
        new AzureBlobProviderInfo("ttdaltinndatastrg02", "data2"),
        "ttd",
        2,
        false,
        _created
    );

    private readonly Mock<IStorageAccountRepository> _repository = CreateRepository();
    private readonly Mock<IStorageAccountResolver> _resolver = new(MockBehavior.Strict);

    [Fact]
    public async Task GetAll_ReturnsRowsWithProviderInfoDocuments()
    {
        _repository
            .Setup(r => r.ReadAll(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Rows(_azureRow, _readOnlyRow));
        HttpClient client = GetClient();

        HttpResponseMessage response = await client.GetAsync(_basePath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = await ReadJson(response);
        Assert.Equal(2, body.RootElement.GetArrayLength());
        JsonElement azure = body.RootElement[0];
        Assert.Equal(3, azure.GetProperty("id").GetInt16());
        Assert.Equal("azure-blob", azure.GetProperty("provider").GetString());
        Assert.Equal(
            "ttdaltinndatastorage",
            azure.GetProperty("providerInfo").GetProperty("accountName").GetString()
        );
        Assert.Equal(
            "data",
            azure.GetProperty("providerInfo").GetProperty("container").GetString()
        );
        Assert.False(azure.GetProperty("providerInfo").TryGetProperty("endpoint", out _));
        Assert.Equal("ttd", azure.GetProperty("org").GetString());
        Assert.Equal(1, azure.GetProperty("storageAccountNumber").GetInt32());
        Assert.True(azure.GetProperty("writable").GetBoolean());
        Assert.Equal(_created, azure.GetProperty("created").GetDateTimeOffset());
        JsonElement readOnly = body.RootElement[1];
        Assert.Equal("azure-blob", readOnly.GetProperty("provider").GetString());
        Assert.Equal(
            "ttdaltinndatastrg02",
            readOnly.GetProperty("providerInfo").GetProperty("accountName").GetString()
        );
        Assert.False(readOnly.GetProperty("writable").GetBoolean());
    }

    [Fact]
    public async Task GetAllGetAndSetWritable_UnreadableRow_ReturnRowWithError()
    {
        UnreadableStorageAccountRow unreadable = new(
            2,
            "unknown-provider",
            "ttd",
            1,
            true,
            _created,
            "The provider is not supported."
        );
        _repository
            .Setup(r => r.ReadAll(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageAccountRows([_azureRow], [unreadable]));
        _repository
            .Setup(r => r.ReadOne(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageAccountRows([], [unreadable with { Writable = false }]));
        _repository
            .Setup(r => r.SetWritable(2, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _resolver.Setup(r => r.Invalidate());
        HttpClient client = GetClient();

        HttpResponseMessage listResponse = await client.GetAsync(_basePath);
        HttpResponseMessage getResponse = await client.GetAsync($"{_basePath}/2");
        HttpResponseMessage putResponse = await client.PutAsJsonAsync(
            $"{_basePath}/2/writable",
            new { writable = false }
        );

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using JsonDocument list = await ReadJson(listResponse);
        Assert.Equal(2, list.RootElement.GetArrayLength());
        JsonElement listed = list.RootElement[0];
        Assert.Equal(2, listed.GetProperty("id").GetInt16());
        Assert.Equal("unknown-provider", listed.GetProperty("provider").GetString());
        Assert.Equal(JsonValueKind.Null, listed.GetProperty("providerInfo").ValueKind);
        Assert.Equal("The provider is not supported.", listed.GetProperty("error").GetString());
        Assert.True(listed.GetProperty("writable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, list.RootElement[1].GetProperty("error").ValueKind);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        using JsonDocument got = await ReadJson(getResponse);
        Assert.Equal(
            "The provider is not supported.",
            got.RootElement.GetProperty("error").GetString()
        );
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        using JsonDocument put = await ReadJson(putResponse);
        Assert.False(put.RootElement.GetProperty("writable").GetBoolean());
        _resolver.Verify(r => r.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task Get_ExistingRow_ReturnsRow()
    {
        _repository
            .Setup(r => r.ReadOne(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Rows(_readOnlyRow));
        HttpClient client = GetClient();

        HttpResponseMessage response = await client.GetAsync($"{_basePath}/4");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = await ReadJson(response);
        Assert.Equal(4, body.RootElement.GetProperty("id").GetInt16());
        Assert.Equal(2, body.RootElement.GetProperty("storageAccountNumber").GetInt32());
    }

    [Fact]
    public async Task Get_MissingRow_ReturnsNotFound()
    {
        _repository.Setup(r => r.ReadOne(9, It.IsAny<CancellationToken>())).ReturnsAsync(Rows());
        HttpClient client = GetClient();

        HttpResponseMessage response = await client.GetAsync($"{_basePath}/9");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using JsonDocument body = await ReadJson(response);
        Assert.Equal(
            "Storage account 9 does not exist.",
            body.RootElement.GetProperty("detail").GetString()
        );
    }

    [Fact]
    public async Task Post_ValidRequest_InsertsRowAndInvalidatesResolver()
    {
        AzureBlobProviderInfo expected = new("ttdaltinndatastorage", "data");
        _repository
            .Setup(r => r.Insert(expected, "ttd", 1, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((short)5);
        _repository
            .Setup(r => r.ReadOne(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Rows(new StorageAccountRow(5, expected, "ttd", 1, true, _created)));
        _resolver.Setup(r => r.Invalidate());
        HttpClient client = GetClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            _basePath,
            new
            {
                provider = "azure-blob",
                providerInfo = new { accountName = "ttdaltinndatastorage", container = "data" },
                org = "ttd",
                storageAccountNumber = 1,
            }
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.EndsWith($"{_basePath}/5", response.Headers.Location?.ToString());
        using JsonDocument body = await ReadJson(response);
        Assert.Equal(5, body.RootElement.GetProperty("id").GetInt16());
        Assert.Equal(
            "data",
            body.RootElement.GetProperty("providerInfo").GetProperty("container").GetString()
        );
        Assert.False(
            body.RootElement.GetProperty("providerInfo").TryGetProperty("endpoint", out _)
        );
        _repository.Verify(
            r => r.Insert(expected, "ttd", 1, true, It.IsAny<CancellationToken>()),
            Times.Once
        );
        _resolver.Verify(r => r.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task Post_NotWritable_InsertsReadOnlyRow()
    {
        AzureBlobProviderInfo expected = new("ttdaltinndatastrg02", "data2");
        _repository
            .Setup(r => r.Insert(expected, "ttd", 2, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((short)6);
        _repository
            .Setup(r => r.ReadOne(6, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Rows(new StorageAccountRow(6, expected, "ttd", 2, false, _created)));
        _resolver.Setup(r => r.Invalidate());
        HttpClient client = GetClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            _basePath,
            new
            {
                provider = "azure-blob",
                providerInfo = new { accountName = "ttdaltinndatastrg02", container = "data2" },
                org = "ttd",
                storageAccountNumber = 2,
                writable = false,
            }
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using JsonDocument body = await ReadJson(response);
        Assert.False(body.RootElement.GetProperty("writable").GetBoolean());
    }

    public static TheoryData<string, string> InvalidRequests =>
        new()
        {
            {
                """{"provider":"s3","providerInfo":{"bucket":"b"},"org":"ttd","storageAccountNumber":1}""",
                "Storage account provider 's3' is not supported."
            },
            {
                """{"provider":"file-system","providerInfo":{"directory":"/data/blobs/ttd"},"org":"ttd","storageAccountNumber":1}""",
                "Storage account provider 'file-system' is not supported."
            },
            {
                """{"provider":"azure-blob","providerInfo":{"accountName":"a"},"org":"ttd","storageAccountNumber":1}""",
                "ProviderInfo is not valid for provider 'azure-blob': "
            },
            {
                """{"provider":"azure-blob","providerInfo":{"accountName":"ttdaccount","container":"data","endpoint":"https://evil.example"},"org":"ttd","storageAccountNumber":1}""",
                "ProviderInfo is not valid for provider 'azure-blob': "
            },
            {
                """{"provider":"azure-blob","providerInfo":{"accountName":"evil.example/x","container":"data"},"org":"ttd","storageAccountNumber":1}""",
                "ProviderInfo is not valid for provider 'azure-blob': "
            },
            {
                """{"provider":"azure-blob","providerInfo":{"accountName":"a","container":"c"},"storageAccountNumber":1}""",
                "Org is required."
            },
            {
                """{"provider":"azure-blob","providerInfo":{"accountName":"a","container":"c"},"org":" ","storageAccountNumber":1}""",
                "Org is required."
            },
            {
                """{"provider":"azure-blob","providerInfo":{"accountName":"a","container":"c"},"org":"ttd","storageAccountNumber":0}""",
                "StorageAccountNumber must be 1 or greater."
            },
            {
                """{"providerInfo":{"accountName":"a","container":"c"},"org":"ttd","storageAccountNumber":1}""",
                "Provider is required."
            },
            {
                """{"provider":"azure-blob","org":"ttd","storageAccountNumber":1}""",
                "ProviderInfo is required."
            },
        };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Post_InvalidRequest_ReturnsBadRequestWithoutInserting(
        string json,
        string expectedDetail
    )
    {
        HttpClient client = GetClient();

        HttpResponseMessage response = await client.PostAsync(
            _basePath,
            new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument body = await ReadJson(response);
        Assert.StartsWith(expectedDetail, body.RootElement.GetProperty("detail").GetString());
        VerifyNoChanges();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PutWritable_ExistingRow_SetsFlagAndInvalidatesResolver(bool writable)
    {
        _repository
            .Setup(r => r.SetWritable(4, writable, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repository
            .Setup(r => r.ReadOne(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Rows(_readOnlyRow with { Writable = writable }));
        _resolver.Setup(r => r.Invalidate());
        HttpClient client = GetClient();

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{_basePath}/4/writable",
            new { writable }
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = await ReadJson(response);
        Assert.Equal(writable, body.RootElement.GetProperty("writable").GetBoolean());
        _repository.Verify(
            r => r.SetWritable(4, writable, It.IsAny<CancellationToken>()),
            Times.Once
        );
        _resolver.Verify(r => r.Invalidate(), Times.Once);
    }

    [Fact]
    public async Task PutWritable_MissingRow_ReturnsNotFound()
    {
        _repository
            .Setup(r => r.SetWritable(9, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        HttpClient client = GetClient();

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{_basePath}/9/writable",
            new { writable = true }
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        _resolver.Verify(r => r.Invalidate(), Times.Never);
    }

    [Fact]
    public async Task PutWritable_MissingFlag_ReturnsBadRequest()
    {
        HttpClient client = GetClient();

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{_basePath}/4/writable",
            new { }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        VerifyNoChanges();
    }

    [Fact]
    public async Task Post_ConcurrentWritableHandOver_ReturnsConflict()
    {
        AzureBlobProviderInfo expected = new("ttdaltinndatastrg02", "data2");
        _repository
            .Setup(r => r.Insert(expected, "ttd", 2, true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(UniqueViolation("storageaccounts_writable"));
        HttpClient client = GetClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            _basePath,
            new
            {
                provider = "azure-blob",
                providerInfo = new { accountName = "ttdaltinndatastrg02", container = "data2" },
                org = "ttd",
                storageAccountNumber = 2,
            }
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using JsonDocument body = await ReadJson(response);
        Assert.Equal(409, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(
            "Another writable storage account for org 'ttd' number 2 was set at the same time; retry.",
            body.RootElement.GetProperty("detail").GetString()
        );
        _resolver.Verify(r => r.Invalidate(), Times.Never);
    }

    [Fact]
    public async Task PutWritable_ConcurrentWritableHandOver_ReturnsConflict()
    {
        _repository
            .Setup(r => r.SetWritable(4, true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(UniqueViolation("storageaccounts_writable"));
        _repository
            .Setup(r => r.ReadOne(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Rows(_readOnlyRow));
        HttpClient client = GetClient();

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{_basePath}/4/writable",
            new { writable = true }
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using JsonDocument body = await ReadJson(response);
        Assert.Equal(
            "Another writable storage account for org 'ttd' number 2 was set at the same time; retry.",
            body.RootElement.GetProperty("detail").GetString()
        );
        _resolver.Verify(r => r.Invalidate(), Times.Never);
    }

    [Fact]
    public async Task PutWritable_OtherUniqueViolation_ReturnsInternalServerError()
    {
        _repository
            .Setup(r => r.SetWritable(4, true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(UniqueViolation("storageaccounts_pkey"));
        HttpClient client = GetClient();

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{_basePath}/4/writable",
            new { writable = true }
        );

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        _resolver.Verify(r => r.Invalidate(), Times.Never);
    }

    [Fact]
    public async Task GetAll_WithoutToken_ReturnsUnauthorized()
    {
        HttpClient client = GetClient(token: null);

        HttpResponseMessage response = await client.GetAsync(_basePath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public static TheoryData<string, string> EndpointsRequiringScope =>
        new()
        {
            { "GET", _basePath },
            { "GET", $"{_basePath}/4" },
            { "POST", _basePath },
            { "PUT", $"{_basePath}/4/writable" },
        };

    [Theory]
    [MemberData(nameof(EndpointsRequiringScope))]
    public async Task Endpoints_WithoutAdminScope_ReturnForbidden(string method, string path)
    {
        HttpClient client = GetClient(PrincipalUtil.GetOrgToken("ttd", scope: "altinn:appdeploy"));
        using HttpRequestMessage request = new(new HttpMethod(method), path);
        if (method != "GET")
        {
            request.Content = new StringContent(
                "{}",
                System.Text.Encoding.UTF8,
                "application/json"
            );
        }

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        VerifyNoChanges();
    }

    /// <summary>
    /// The host's startup check reads all rows, so the strict mock answers that with no rows unless
    /// a test sets up rows of its own.
    /// </summary>
    private static StorageAccountRows Rows(params StorageAccountRow[] rows) => new(rows, []);

    private static PostgresException UniqueViolation(string constraintName) =>
        new(
            "duplicate key value violates unique constraint",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.UniqueViolation,
            constraintName: constraintName
        );

    private static Mock<IStorageAccountRepository> CreateRepository()
    {
        Mock<IStorageAccountRepository> repository = new(MockBehavior.Strict);
        repository.Setup(r => r.ReadAll(It.IsAny<CancellationToken>())).ReturnsAsync(Rows());
        return repository;
    }

    private void VerifyNoChanges()
    {
        _repository.Verify(
            r =>
                r.Insert(
                    It.IsAny<StorageAccountProviderInfo>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
        _repository.Verify(
            r => r.SetWritable(It.IsAny<short>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    private static async Task<JsonDocument> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private HttpClient GetClient() =>
        GetClient(
            PrincipalUtil.GetOrgToken("digdir", scope: AuthzConstants.SCOPE_STORAGEACCOUNTS_ADMIN)
        );

    private HttpClient GetClient(string? token)
    {
        HttpClient client = factory
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton(_repository.Object);
                    services.AddSingleton(_resolver.Object);
                    services.AddSingleton<
                        IPublicSigningKeyProvider,
                        PublicSigningKeyProviderMock
                    >();
                    services.AddSingleton<
                        IPostConfigureOptions<JwtCookieOptions>,
                        JwtCookiePostConfigureOptionsStub
                    >();
                    services.AddSingleton(new Mock<IMessageBus>().Object);
                });
            })
            .CreateClient();

        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                token
            );
        }

        return client;
    }
}
