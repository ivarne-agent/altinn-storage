using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingServices;

public class BlobVersionRepointServiceTests
{
    private const string _org = "ttd";
    private const string _appId = "ttd/app";
    private const short _sourceId = 5;
    private const short _targetId = 7;

    private static readonly DateTimeOffset _created = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    private static readonly StorageAccountRow _source = new(
        _sourceId,
        new AzureBlobProviderInfo("ttdaltinnprodstrg01", "ttd-prod-appsdata-blob-db"),
        _org,
        1,
        false,
        _created
    );

    private static readonly StorageAccountRow _target = new(
        _targetId,
        new AzureBlobProviderInfo("ttdaltinnprodstrg02", "ttd-prod-appsdata-blob-db"),
        _org,
        1,
        true,
        _created
    );

    private readonly Mock<IBlobVersionRepointRepository> _repointRepository = new(
        MockBehavior.Strict
    );
    private readonly Mock<IStorageAccountRepository> _accountRepository = new(MockBehavior.Strict);
    private readonly Mock<IBlobRepository> _blobRepository = new(MockBehavior.Strict);

    public BlobVersionRepointServiceTests()
    {
        _accountRepository
            .Setup(r => r.ReadOne(_sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageAccountRows([_source], []));
        _accountRepository
            .Setup(r => r.ReadOne(_targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageAccountRows([_target], []));
    }

    [Fact]
    public async Task Repoint_DryRun_CountsWithoutUpdating()
    {
        List<BlobVersionRepointRow> rows = [Row(1, null), Row(2, null), Row(3, null)];
        SetupPages(null, rows);
        SetupExists(rows[0], rows[2]);

        BlobVersionRepointResult result = await CreateService()
            .Repoint(PatternJob(dryRun: true, batchSize: 10));

        BlobVersionRepointSummary summary = Assert.IsType<BlobVersionRepointSummary>(
            result.Summary
        );
        Assert.True(summary.DryRun);
        Assert.Equal(3, summary.Examined);
        Assert.Equal(2, summary.Repointed);
        Assert.Equal(1, summary.MissingInTarget);
        Assert.Equal([rows[1].Id], summary.MissingInTargetIds);
        Assert.Equal(0, summary.AlreadyMoved);
        Assert.Equal(rows[2].Id, summary.LastId);
        Assert.True(summary.Completed);
        VerifyNoUpdates();
    }

    [Fact]
    public async Task Repoint_PatternDefault_ReadsByOrgAndNumberAndUpdatesFromNull()
    {
        List<BlobVersionRepointRow> rows = [Row(1, null), Row(2, null)];
        SetupPages(null, rows);
        SetupExists(rows[0]);
        SetupUpdate(rows[0].Id, null, true);

        BlobVersionRepointResult result = await CreateService()
            .Repoint(PatternJob(dryRun: false, batchSize: 10));

        Assert.Equal(1, result.Summary!.Repointed);
        Assert.Equal(1, result.Summary.MissingInTarget);
        _repointRepository.Verify(
            r => r.ReadPage(null, _org, 1, null, Guid.Empty, 10, It.IsAny<CancellationToken>()),
            Times.Once
        );
        _repointRepository.Verify(
            r =>
                r.UpdateAccount(
                    rows[1].Id,
                    It.IsAny<short?>(),
                    It.IsAny<short>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task Repoint_RowChangedConcurrently_CountsAlreadyMoved()
    {
        List<BlobVersionRepointRow> rows = [Row(1, _sourceId), Row(2, _sourceId)];
        SetupPages(_sourceId, rows);
        SetupExists(rows[0], rows[1]);
        SetupUpdate(rows[0].Id, _sourceId, true);
        SetupUpdate(rows[1].Id, _sourceId, false);

        BlobVersionRepointResult result = await CreateService()
            .Repoint(AccountJob(dryRun: false, batchSize: 10));

        Assert.Equal(2, result.Summary!.Examined);
        Assert.Equal(1, result.Summary.Repointed);
        Assert.Equal(1, result.Summary.AlreadyMoved);
        Assert.Equal(0, result.Summary.MissingInTarget);
    }

    [Fact]
    public async Task Repoint_ChecksTheVersionedPathInTheTargetAccount()
    {
        BlobVersionRepointRow row = Row(1, _sourceId);
        SetupPages(_sourceId, [row]);
        SetupExists(row);
        SetupUpdate(row.Id, _sourceId, true);

        await CreateService().Repoint(AccountJob(dryRun: false, batchSize: 10));

        _blobRepository.Verify(
            r =>
                r.BlobExists(
                    It.Is<StorageAccount>(a =>
                        a.Id == _targetId && a.ProviderInfo == _target.ProviderInfo
                    ),
                    $"{_appId}/{row.InstanceGuid}/data-elements/{BlobVersionId.Encode(row.Id)}",
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Fact]
    public async Task Repoint_PagesUntilAShortPage()
    {
        List<BlobVersionRepointRow> rows = Enumerable
            .Range(1, 5)
            .Select(i => Row(i, null))
            .ToList();
        SetupPages(null, rows);
        SetupExists([.. rows]);
        foreach (BlobVersionRepointRow row in rows)
        {
            SetupUpdate(row.Id, null, true);
        }

        BlobVersionRepointResult result = await CreateService()
            .Repoint(PatternJob(dryRun: false, batchSize: 2, maxBatches: 10));

        Assert.Equal(5, result.Summary!.Examined);
        Assert.Equal(5, result.Summary.Repointed);
        Assert.Equal(rows[4].Id, result.Summary.LastId);
        Assert.True(result.Summary.Completed);
        _repointRepository.Verify(
            r =>
                r.ReadPage(
                    It.IsAny<short?>(),
                    It.IsAny<string?>(),
                    It.IsAny<int?>(),
                    It.IsAny<string?>(),
                    It.IsAny<Guid>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Exactly(3)
        );
    }

    [Fact]
    public async Task Repoint_StopsAtMaxBatchesAndResumesAfterLastId()
    {
        List<BlobVersionRepointRow> rows = Enumerable
            .Range(1, 5)
            .Select(i => Row(i, null))
            .ToList();
        SetupPages(null, rows);
        SetupExists([.. rows]);
        BlobVersionRepointService service = CreateService();

        BlobVersionRepointResult first = await service.Repoint(
            PatternJob(dryRun: true, batchSize: 2, maxBatches: 1)
        );
        BlobVersionRepointResult second = await service.Repoint(
            PatternJob(dryRun: true, batchSize: 2, maxBatches: 10) with
            {
                AfterId = first.Summary!.LastId,
            }
        );

        Assert.Equal(2, first.Summary.Examined);
        Assert.Equal(rows[1].Id, first.Summary.LastId);
        Assert.False(first.Summary.Completed);
        Assert.Equal(3, second.Summary!.Examined);
        Assert.Equal(rows[4].Id, second.Summary.LastId);
        Assert.True(second.Summary.Completed);
    }

    [Fact]
    public async Task Repoint_NoRowsAfterAfterId_KeepsAfterIdAsLastId()
    {
        SetupPages(null, []);
        Guid afterId = Guid.NewGuid();

        BlobVersionRepointResult result = await CreateService()
            .Repoint(PatternJob(dryRun: false, batchSize: 10) with { AfterId = afterId });

        Assert.Equal(0, result.Summary!.Examined);
        Assert.Equal(afterId, result.Summary.LastId);
        Assert.True(result.Summary.Completed);
    }

    [Fact]
    public async Task Repoint_UnknownTarget_IsRejected()
    {
        _accountRepository
            .Setup(r => r.ReadOne(9, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageAccountRows([], []));

        BlobVersionRepointResult result = await CreateService()
            .Repoint(PatternJob(dryRun: false, batchSize: 10) with { ToAccountId = 9 });

        Assert.Null(result.Summary);
        Assert.Equal("Storage account 9 does not exist.", result.Error);
        VerifyNoReads();
    }

    [Fact]
    public async Task Repoint_UnreadableTarget_IsRejected()
    {
        UnreadableStorageAccountRow unreadable = new(
            9,
            "unknown-provider",
            _org,
            1,
            true,
            _created,
            "The provider is not supported."
        );
        _accountRepository
            .Setup(r => r.ReadOne(9, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageAccountRows([], [unreadable]));

        BlobVersionRepointResult result = await CreateService()
            .Repoint(PatternJob(dryRun: false, batchSize: 10) with { ToAccountId = 9 });

        Assert.Null(result.Summary);
        Assert.Equal(unreadable.Description, result.Error);
        VerifyNoReads();
    }

    [Fact]
    public async Task Repoint_UnreadableSource_ReadsByItsAccountId()
    {
        _accountRepository
            .Setup(r => r.ReadOne(_sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new StorageAccountRows(
                    [],
                    [
                        new UnreadableStorageAccountRow(
                            _sourceId,
                            "unknown-provider",
                            _org,
                            1,
                            false,
                            _created,
                            "The provider is not supported."
                        ),
                    ]
                )
            );
        List<BlobVersionRepointRow> rows = [Row(1, _sourceId)];
        SetupPages(_sourceId, rows);
        SetupExists(rows[0]);
        SetupUpdate(rows[0].Id, _sourceId, true);

        BlobVersionRepointResult result = await CreateService()
            .Repoint(AccountJob(dryRun: false, batchSize: 10));

        Assert.Null(result.Error);
        Assert.Equal(1, result.Summary!.Repointed);
    }

    [Fact]
    public async Task Repoint_UnknownSource_IsRejected()
    {
        _accountRepository
            .Setup(r => r.ReadOne(9, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageAccountRows([], []));

        BlobVersionRepointResult result = await CreateService()
            .Repoint(AccountJob(dryRun: false, batchSize: 10) with { FromAccountId = 9 });

        Assert.Equal("Storage account 9 does not exist.", result.Error);
        VerifyNoReads();
    }

    [Theory]
    [InlineData("digdir", 1)]
    [InlineData(_org, 2)]
    public async Task Repoint_TargetServingAnotherPair_IsRejected(string org, int number)
    {
        BlobVersionRepointResult result = await CreateService()
            .Repoint(
                PatternJob(dryRun: false, batchSize: 10) with
                {
                    Org = org,
                    StorageAccountNumber = number,
                }
            );

        Assert.Equal(
            $"Storage account {_targetId} serves org '{_org}' number 1, not org '{org}' number {number}.",
            result.Error
        );
        VerifyNoReads();
    }

    private BlobVersionRepointService CreateService() =>
        new(
            _repointRepository.Object,
            _accountRepository.Object,
            _blobRepository.Object,
            NullLogger<BlobVersionRepointService>.Instance
        );

    private static BlobVersionRepointJob PatternJob(
        bool dryRun,
        int batchSize,
        int maxBatches = 10
    ) => new(null, _org, 1, _targetId, null, batchSize, maxBatches, dryRun, null);

    private static BlobVersionRepointJob AccountJob(bool dryRun, int batchSize) =>
        new(_sourceId, null, null, _targetId, null, batchSize, 10, dryRun, null);

    private static BlobVersionRepointRow Row(int index, short? storageAccountId) =>
        new(
            new Guid($"00000000-0000-0000-0000-{index:D12}"),
            Guid.NewGuid(),
            Guid.NewGuid(),
            _appId,
            _org,
            null,
            storageAccountId
        );

    /// <summary>
    /// Serves the rows as the database would: in id order, after the requested id, at most the limit.
    /// </summary>
    private void SetupPages(short? fromAccountId, IReadOnlyList<BlobVersionRepointRow> rows)
    {
        _repointRepository
            .Setup(r =>
                r.ReadPage(
                    fromAccountId,
                    It.IsAny<string?>(),
                    It.IsAny<int?>(),
                    It.IsAny<string?>(),
                    It.IsAny<Guid>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                (
                    short? _,
                    string? _,
                    int? _,
                    string? _,
                    Guid afterId,
                    int limit,
                    CancellationToken _
                ) => rows.Where(row => row.Id.CompareTo(afterId) > 0).Take(limit).ToList()
            );
    }

    private void SetupExists(params BlobVersionRepointRow[] present)
    {
        HashSet<string> paths =
        [
            .. present.Select(row =>
                DataElementHelper.GetVersionedBlobPath(
                    row.AppId,
                    row.InstanceGuid,
                    BlobVersionId.Encode(row.Id)
                )
            ),
        ];
        _blobRepository
            .Setup(r =>
                r.BlobExists(
                    It.IsAny<StorageAccount>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                (StorageAccount _, string path, CancellationToken _) => paths.Contains(path)
            );
    }

    private void SetupUpdate(Guid id, short? fromAccountId, bool updated)
    {
        _repointRepository
            .Setup(r =>
                r.UpdateAccount(id, fromAccountId, _targetId, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(updated);
    }

    private void VerifyNoUpdates()
    {
        _repointRepository.Verify(
            r =>
                r.UpdateAccount(
                    It.IsAny<Guid>(),
                    It.IsAny<short?>(),
                    It.IsAny<short>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    private void VerifyNoReads()
    {
        _repointRepository.Verify(
            r =>
                r.ReadPage(
                    It.IsAny<short?>(),
                    It.IsAny<string?>(),
                    It.IsAny<int?>(),
                    It.IsAny<string?>(),
                    It.IsAny<Guid>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }
}
