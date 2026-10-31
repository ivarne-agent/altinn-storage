using System.Threading;
using System.Threading.Tasks;

namespace Altinn.Platform.Storage.Authorization.AppCaller;

/// <summary>
/// Validates the <c>Altinn-App-Token</c> sent by Altinn apps.
/// </summary>
public interface IAppCallerTokenValidator
{
    /// <summary>
    /// Validates a token and checks that it was issued to the expected app.
    /// </summary>
    /// <param name="token">The compact JWS from the header, or null when the header is absent.</param>
    /// <param name="expectedAppId">The <c>{org}/{app}</c> the request targets.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The validation result.</returns>
    Task<AppCallerValidationResult> ValidateAsync(
        string? token,
        string expectedAppId,
        CancellationToken cancellationToken
    );
}
