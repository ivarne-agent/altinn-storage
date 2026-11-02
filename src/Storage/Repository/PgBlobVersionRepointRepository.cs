using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;
using Npgsql;
using NpgsqlTypes;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Represents an implementation of <see cref="IBlobVersionRepointRepository"/>.
/// </summary>
public class PgBlobVersionRepointRepository(NpgsqlDataSource dataSource)
    : IBlobVersionRepointRepository
{
    private const string _readPageSql =
        "select * from storage.readblobversionsforrepoint($1, $2, $3, $4, $5, $6)";
    private const string _updateAccountSql = "select storage.updateblobversionaccount($1, $2, $3)";

    /// <inheritdoc/>
    public async Task<IReadOnlyList<BlobVersionRepointRow>> ReadPage(
        short? fromAccountId,
        string? org,
        int? storageAccountNumber,
        string? appId,
        Guid afterId,
        int limit,
        CancellationToken cancellationToken = default
    )
    {
        await using NpgsqlCommand pgcom = dataSource.CreateCommand(_readPageSql);
        pgcom.Parameters.AddWithValue(
            NpgsqlDbType.Smallint,
            (object?)fromAccountId ?? DBNull.Value
        );
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)org ?? DBNull.Value);
        pgcom.Parameters.AddWithValue(
            NpgsqlDbType.Integer,
            (object?)storageAccountNumber ?? DBNull.Value
        );
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)appId ?? DBNull.Value);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, afterId);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Integer, limit);

        List<BlobVersionRepointRow> rows = [];
        await using NpgsqlDataReader reader = await pgcom.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(
                new BlobVersionRepointRow(
                    await reader.GetFieldValueAsync<Guid>("id", cancellationToken),
                    await reader.GetFieldValueAsync<Guid>("instanceguid", cancellationToken),
                    await reader.GetFieldValueAsync<Guid>("dataelementid", cancellationToken),
                    await reader.GetFieldValueAsync<string>("appid", cancellationToken),
                    await reader.GetFieldValueAsync<string>("blobstorageorg", cancellationToken),
                    await reader.IsDBNullAsync("storageaccountnumber", cancellationToken)
                        ? null
                        : await reader.GetFieldValueAsync<int>(
                            "storageaccountnumber",
                            cancellationToken
                        ),
                    await reader.IsDBNullAsync("storageaccountid", cancellationToken)
                        ? null
                        : await reader.GetFieldValueAsync<short>(
                            "storageaccountid",
                            cancellationToken
                        )
                )
            );
        }

        return rows;
    }

    /// <inheritdoc/>
    public async Task<bool> UpdateAccount(
        Guid id,
        short? fromAccountId,
        short toAccountId,
        CancellationToken cancellationToken = default
    )
    {
        await using NpgsqlCommand pgcom = dataSource.CreateCommand(_updateAccountSql);
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Uuid, id);
        pgcom.Parameters.AddWithValue(
            NpgsqlDbType.Smallint,
            (object?)fromAccountId ?? DBNull.Value
        );
        pgcom.Parameters.AddWithValue(NpgsqlDbType.Smallint, toAccountId);

        object? updated = await pgcom.ExecuteScalarAsync(cancellationToken);
        return (int)updated! == 1;
    }
}
