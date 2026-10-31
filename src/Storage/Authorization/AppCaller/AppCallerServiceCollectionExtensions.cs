using System;
using Altinn.Platform.Storage.Configuration;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Altinn.Platform.Storage.Authorization.AppCaller;

/// <summary>
/// Registers app caller token validation.
/// </summary>
public static class AppCallerServiceCollectionExtensions
{
    /// <summary>
    /// The configuration section for <see cref="AppCallerSettings"/>.
    /// </summary>
    public const string SectionName = "AppCallerSettings";

    /// <summary>
    /// Registers the validator and key set, and, when a Key Vault is configured and the mode is
    /// not <see cref="AppCallerMode.Off"/>, the Key Vault key source, poller and health check.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddAppCallerTokenValidation(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        IConfigurationSection section = configuration.GetSection(SectionName);
        services.Configure<AppCallerSettings>(section);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<AppTokenKeySet>();
        services.AddSingleton<IAppTokenSigningKeyProvider>(sp =>
            sp.GetRequiredService<AppTokenKeySet>()
        );
        services.AddSingleton<IAppCallerTokenValidator, AppTokenValidator>();

        AppCallerSettings settings = section.Get<AppCallerSettings>() ?? new AppCallerSettings();
        if (settings.Mode == AppCallerMode.Off || settings.KeyVaultUri is null)
        {
            return services;
        }

        Uri keyVaultUri = settings.KeyVaultUri;
        services.AddSingleton(_ => new KeyClient(keyVaultUri, new DefaultAzureCredential()));
        services.AddSingleton<IAppTokenKeySource, KeyVaultAppTokenKeySource>();
        services.AddHostedService<AppTokenKeyPoller>();
        services
            .AddHealthChecks()
            .AddCheck<AppTokenKeySetHealthCheck>("app_token_key_set_health_check");

        return services;
    }
}
