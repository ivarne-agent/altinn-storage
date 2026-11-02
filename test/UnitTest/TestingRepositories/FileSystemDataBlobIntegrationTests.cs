#nullable disable

using System;
using System.IO;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Extensions;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Repository.BlobStorage;
using Altinn.Platform.Storage.Services;
using Altinn.Platform.Storage.UnitTest.Extensions;
using Altinn.Platform.Storage.UnitTest.Mocks;
using Altinn.Platform.Storage.UnitTest.Utils;
using AltinnCore.Authentication.Constants;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

/// <summary>
/// Postgres, Azurite and the file system behind one blob repository: one org has a writable
/// file-system account row, another has no row and uses the Azurite pattern default.
/// </summary>
[Collection("StoragePostgreSQL")]
public sealed class FileSystemDataBlobIntegrationTests
    : IClassFixture<DataElementFixture>,
        IClassFixture<BlobRepositoryAzuriteFixture>,
        IDisposable
{
    private readonly DataElementFixture _dataElementFixture;
    private readonly BlobRepositoryAzuriteFixture _blobFixture;
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "file-system-integration",
        Guid.NewGuid().ToString("N")
    );

    private readonly MemoryCache _memoryCache = new(new MemoryCacheOptions());
    private readonly BlobRepository _blobRepository;

    public FileSystemDataBlobIntegrationTests(
        DataElementFixture dataElementFixture,
        BlobRepositoryAzuriteFixture blobFixture
    )
    {
        _dataElementFixture = dataElementFixture;
        _blobFixture = blobFixture;
        _blobRepository = new BlobRepository([
            new AzureBlobProvider(
                _memoryCache,
                Options.Create(_blobFixture.Configuration),
                NullLogger<AzureBlobProvider>.Instance
            ),
            new FileSystemBlobProvider(
                Options.Create(
                    new StorageAccountSettings { FileSystem = new() { RootDirectory = _root } }
                ),
                NullLogger<FileSystemBlobProvider>.Instance
            ),
        ]);
    }

    public void Dispose()
    {
        _memoryCache.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task UploadReadOverwriteDelete_FileSystemAndAzuriteAccounts_EachBlobStaysInItsAccount()
    {
        // Arrange
        string fileSystemOrg = $"fs{Guid.NewGuid():N}"[..12];
        string azuriteOrg = $"az{Guid.NewGuid():N}"[..12];
        string directory = Path.Combine(_root, fileSystemOrg);
        PgStorageAccountRepository accountRepository = new(_dataElementFixture.DataSource);
        short accountId = await accountRepository.Insert(
            new FileSystemProviderInfo(directory),
            fileSystemOrg,
            StorageAccountNaming.DefaultStorageAccountNumber,
            writable: true
        );
        BlobContainerClient azuriteContainer = await BlobRepositoryAzuriteFixture.CreateContainer(
            string.Format(_blobFixture.Configuration.OrgStorageContainer, azuriteOrg)
        );
        IStorageAccountResolver resolver = TestStorageAccounts.CreateResolver(
            new PgStorageAccountRepository(_dataElementFixture.DataSource),
            _blobFixture.Configuration
        );

        try
        {
            // Act
            Lifecycle fileSystem = await RunLifecycle(
                resolver,
                fileSystemOrg,
                path => Task.FromResult(File.Exists(Path.Combine(directory, path)))
            );
            Lifecycle azurite = await RunLifecycle(
                resolver,
                azuriteOrg,
                async path => (bool)await azuriteContainer.GetBlobClient(path).ExistsAsync()
            );

            // Assert
            Assert.Equal(accountId, fileSystem.Uploaded.StorageAccount.Id);
            Assert.Equal(
                new FileSystemProviderInfo(directory),
                fileSystem.Uploaded.StorageAccount.ProviderInfo
            );
            Assert.Null(azurite.Uploaded.StorageAccount.Id);
            Assert.IsType<AzureBlobProviderInfo>(azurite.Uploaded.StorageAccount.ProviderInfo);

            foreach (Lifecycle lifecycle in new[] { fileSystem, azurite })
            {
                Assert.Equal("first content", lifecycle.FirstRead);
                Assert.Equal("second content", lifecycle.SecondRead);
                Assert.NotEqual(lifecycle.FirstPath, lifecycle.SecondPath);
            }

            Assert.Equal(
                Path.Combine(
                    directory,
                    fileSystem.Instance.AppId,
                    fileSystem.Instance.Id.ToString(),
                    "data-elements",
                    fileSystem.Uploaded.DataElement.BlobVersionId
                ),
                Path.Combine(directory, fileSystem.FirstPath)
            );
            Assert.False(await azuriteContainer.GetBlobClient(fileSystem.SecondPath).ExistsAsync());
            Assert.True(fileSystem.FilesBeforeDelete.First);
            Assert.True(fileSystem.FilesBeforeDelete.Second);
            Assert.False(File.Exists(Path.Combine(directory, fileSystem.FirstPath)));
            Assert.False(File.Exists(Path.Combine(directory, fileSystem.SecondPath)));
            Assert.False(await azuriteContainer.GetBlobClient(fileSystem.FirstPath).ExistsAsync());

            Assert.True(azurite.FilesBeforeDelete.First);
            Assert.True(azurite.FilesBeforeDelete.Second);
            Assert.False(await azuriteContainer.GetBlobClient(azurite.FirstPath).ExistsAsync());
            Assert.False(await azuriteContainer.GetBlobClient(azurite.SecondPath).ExistsAsync());
            Assert.False(Directory.Exists(Path.Combine(_root, azuriteOrg)));
        }
        finally
        {
            await azuriteContainer.DeleteIfExistsAsync();
        }

        async Task<Lifecycle> RunLifecycle(
            IStorageAccountResolver resolver,
            string org,
            Func<string, Task<bool>> exists
        )
        {
            InstanceInternal instance = await CreateInstance(org);
            Application application = new()
            {
                Id = instance.AppId,
                Org = org,
                DataTypes = [new DataType { Id = "default" }],
            };
            DataService dataService = new(
                Mock.Of<IFileScanQueueClient>(),
                _dataElementFixture.DataRepo,
                _blobRepository,
                resolver
            );
            int partyId = int.Parse(instance.InstanceOwner.PartyId);

            DataUploadResult uploaded = await dataService.UploadDataAndCreateDataElement(
                instance,
                new MemoryStream(Encoding.UTF8.GetBytes("first content")),
                new DataElementCreateOptions
                {
                    DataElementId = Guid.NewGuid(),
                    DataType = "default",
                    ContentType = "text/plain",
                    Filename = "content.txt",
                    Created = DateTime.UtcNow,
                    CreatedBy = org,
                },
                instance.InternalId,
                application,
                cancellationToken: CancellationToken.None
            );
            Guid dataGuid = uploaded.DataElement.Id;

            string firstRead = await ReadContent(
                await CreateDataController(resolver, dataService, application, org)
                    .Get(partyId, instance.Id, dataGuid, CancellationToken.None)
            );

            DataController overwriteController = CreateDataController(
                resolver,
                dataService,
                application,
                org,
                "second content"
            );
            ActionResult<DataElement> overwritten = await overwriteController.OverwriteData(
                partyId,
                instance.Id,
                dataGuid,
                CancellationToken.None
            );
            DataElement overwrittenElement = Assert.IsType<DataElement>(
                Assert.IsType<OkObjectResult>(overwritten.Result).Value
            );

            string secondRead = await ReadContent(
                await CreateDataController(resolver, dataService, application, org)
                    .Get(partyId, instance.Id, dataGuid, CancellationToken.None)
            );

            string firstPath = uploaded.DataElement.BlobStoragePath;
            string secondPath = overwrittenElement.BlobStoragePath;
            (bool First, bool Second) filesBeforeDelete = (
                await exists(firstPath),
                await exists(secondPath)
            );

            ActionResult<DataElement> deleted = await CreateDataController(
                    resolver,
                    dataService,
                    application,
                    org
                )
                .Delete(partyId, instance.Id, dataGuid, false, CancellationToken.None);
            Assert.IsType<OkObjectResult>(deleted.Result);

            return new Lifecycle(
                instance,
                uploaded,
                firstRead,
                secondRead,
                firstPath,
                secondPath,
                filesBeforeDelete
            );
        }
    }

    private async Task<InstanceInternal> CreateInstance(string org)
    {
        InstanceInternal instance = TestData.Instance_1_1.Clone().FromApiModel();
        instance.Id = Guid.NewGuid();
        instance.Org = org;
        instance.AppId = $"{org}/test-applikasjon-1";
        InstanceInternal created = await _dataElementFixture.InstanceRepo.Create(
            instance,
            CancellationToken.None
        );
        return await _dataElementFixture.InstanceRepo.GetOne(
            created.Id,
            false,
            CancellationToken.None
        );
    }

    private DataController CreateDataController(
        IStorageAccountResolver resolver,
        DataService dataService,
        Application application,
        string org,
        string requestContent = null
    )
    {
        Mock<IApplicationRepository> applicationRepository = new();
        applicationRepository
            .Setup(repository =>
                repository.FindOne(application.Id, application.Org, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(application);
        Mock<IAuthorization> authorization = new();
        authorization
            .Setup(service =>
                service.AuthorizeEnrichedInstanceAction(It.IsAny<InstanceInternal>(), "read")
            )
            .ReturnsAsync(true);
        Mock<IInstanceEventService> eventService = new();
        eventService
            .Setup(service =>
                service.BuildInstanceEvent(
                    It.IsAny<InstanceEventType>(),
                    It.IsAny<InstanceInternal>(),
                    It.IsAny<DataElementInternal>()
                )
            )
            .Returns(
                (InstanceEventType eventType, InstanceInternal _, DataElementInternal element) =>
                    new InstanceEvent
                    {
                        EventType = eventType.ToString(),
                        DataId = element.Id.ToString(),
                        Created = DateTime.UtcNow,
                    }
            );
        DefaultHttpContext httpContext = new()
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [
                        new Claim(AltinnCoreClaimTypes.Org, org),
                        new Claim(AltinnCoreClaimTypes.OrgNumber, "111111111"),
                    ],
                    "test"
                )
            ),
        };
        if (requestContent is not null)
        {
            byte[] body = Encoding.UTF8.GetBytes(requestContent);
            httpContext.Request.ContentType = "text/plain";
            httpContext.Request.ContentLength = body.Length;
            httpContext.Request.Body = new MemoryStream(body);
        }

        return new DataController(
            _dataElementFixture.DataRepo,
            _blobRepository,
            _dataElementFixture.InstanceRepo,
            _dataElementFixture.InstanceMutationRepo,
            applicationRepository.Object,
            dataService,
            eventService.Object,
            Options.Create(new GeneralSettings { Hostname = "https://altinn.no/" }),
            null,
            authorization.Object,
            resolver
        )
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    private static async Task<string> ReadContent(ActionResult result)
    {
        FileStreamResult file = Assert.IsType<FileStreamResult>(result);
        using StreamReader reader = new(file.FileStream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private sealed record Lifecycle(
        InstanceInternal Instance,
        DataUploadResult Uploaded,
        string FirstRead,
        string SecondRead,
        string FirstPath,
        string SecondPath,
        (bool First, bool Second) FilesBeforeDelete
    );
}
