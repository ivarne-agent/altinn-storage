using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingServices;

public class StorageAccountSettingsValidatorTests
{
    [Theory]
    [InlineData(StorageAccountProviderInfo.AzureBlob, "", true)]
    [InlineData(StorageAccountProviderInfo.FileSystem, "/data/blobs", true)]
    [InlineData(StorageAccountProviderInfo.FileSystem, "", false)]
    [InlineData("azure", "/data/blobs", false)]
    [InlineData("", "", false)]
    public void Validate_DefaultProvider_AcceptsKnownProvidersOnly(
        string defaultProvider,
        string rootDirectory,
        bool valid
    )
    {
        StorageAccountSettings settings = new()
        {
            DefaultProvider = defaultProvider,
            FileSystem = new() { RootDirectory = rootDirectory },
        };

        ValidateOptionsResult result = new StorageAccountSettingsValidator().Validate(
            null,
            settings
        );

        Assert.Equal(valid, result.Succeeded);
    }

    [Fact]
    public void Options_UnknownDefaultProvider_ThrowsOptionsValidationException()
    {
        ServiceCollection services = new();
        services.Configure<StorageAccountSettings>(settings => settings.DefaultProvider = "azure");
        services.AddSingleton<
            IValidateOptions<StorageAccountSettings>,
            StorageAccountSettingsValidator
        >();
        using ServiceProvider provider = services.BuildServiceProvider();

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<StorageAccountSettings>>().Value
        );
        Assert.Contains(
            "StorageAccounts:DefaultProvider 'azure' is not supported",
            exception.Message
        );
    }
}
