using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;

namespace Altinn.Platform.Storage.Repository;

/// <summary>
/// Searches application instances. The caller authorizes the search and the instances it finds.
/// </summary>
public interface IInstanceQueryRepository
{
    /// <summary>
    /// Gets fully hydrated storage-domain instances that satisfy the query parameters.
    /// </summary>
    /// <param name="queryParams">the query params</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>The domain query result.</returns>
    Task<InstanceQueryResult> GetInstancesFromQuery(
        InstanceQueryParameters queryParams,
        CancellationToken cancellationToken
    );
}
