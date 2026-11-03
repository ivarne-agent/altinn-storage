using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Npgsql;

namespace Altinn.Platform.Storage.Services;

/// <summary>
/// Manages storage account rows, dropping the resolver cache after a change so write routing on this
/// instance follows it at once.
/// </summary>
public sealed class StorageAccountService(
    IStorageAccountRepository repository,
    IStorageAccountResolver resolver
) : IStorageAccountService
{
    /// <inheritdoc/>
    public Task<StorageAccountRows> GetAll(CancellationToken cancellationToken = default) =>
        repository.ReadAll(cancellationToken);

    /// <inheritdoc/>
    public Task<StorageAccountRows> Get(short id, CancellationToken cancellationToken = default) =>
        repository.ReadOne(id, cancellationToken);

    /// <inheritdoc/>
    public async Task<StorageAccountRow> Create(
        StorageAccountProviderInfo providerInfo,
        string org,
        int storageAccountNumber,
        bool writable,
        CancellationToken cancellationToken = default
    )
    {
        short id;
        try
        {
            id = await repository.Insert(
                providerInfo,
                org,
                storageAccountNumber,
                writable,
                cancellationToken
            );
        }
        catch (PostgresException exception) when (IsWritableConflict(exception))
        {
            throw new StorageAccountWritableConflictException(org, storageAccountNumber, exception);
        }

        resolver.Invalidate();
        StorageAccountRows rows = await repository.ReadOne(id, cancellationToken);
        return rows.Readable.SingleOrDefault()
            ?? throw new InvalidOperationException(
                $"Storage account {id} was not readable after insert."
            );
    }

    /// <inheritdoc/>
    public async Task<StorageAccountRows> SetWritable(
        short id,
        bool writable,
        CancellationToken cancellationToken = default
    )
    {
        bool exists;
        try
        {
            exists = await repository.SetWritable(id, writable, cancellationToken);
        }
        catch (PostgresException exception) when (IsWritableConflict(exception))
        {
            StorageAccountRows rows = await repository.ReadOne(id, cancellationToken);
            (string org, int number) = rows
                .Readable.Select(r => (r.Org, r.StorageAccountNumber))
                .Concat(rows.Unreadable.Select(r => (r.Org, r.StorageAccountNumber)))
                .Single();
            throw new StorageAccountWritableConflictException(org, number, exception);
        }

        if (!exists)
        {
            return new StorageAccountRows([], []);
        }

        resolver.Invalidate();
        return await repository.ReadOne(id, cancellationToken);
    }

    /// <summary>
    /// A concurrent hand-over for the same org and number won the <c>storageaccounts_writable</c>
    /// unique index. Other unique violations are not conflicts the caller can resolve by retrying.
    /// </summary>
    private static bool IsWritableConflict(PostgresException exception) =>
        exception.SqlState == PostgresErrorCodes.UniqueViolation
        && exception.ConstraintName == "storageaccounts_writable";
}
