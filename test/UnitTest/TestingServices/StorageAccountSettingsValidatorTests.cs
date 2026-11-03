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
    [InlineData(StorageAccountProviderInfo.S3, "/data/blobs", false)]
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

    [Fact]
    public void Validate_S3DefaultProvider_ExplainsThatS3IsRowsOnly()
    {
        ValidateOptionsResult result = new StorageAccountSettingsValidator().Validate(
            null,
            new StorageAccountSettings { DefaultProvider = StorageAccountProviderInfo.S3 }
        );

        Assert.True(result.Failed);
        Assert.Contains("explicit storage account rows only", result.FailureMessage);
    }

    [Theory]
    [InlineData("", "", "", true)]
    [InlineData("http://localhost:9000", "minioadmin", "minioadmin", true)]
    [InlineData("https://s3.example.com", "", "", true)]
    [InlineData("", "access", "secret", true)]
    [InlineData("localhost:9000", "", "", false)]
    [InlineData("/relative", "", "", false)]
    [InlineData("ftp://localhost:9000", "", "", false)]
    [InlineData("", "access", "", false)]
    [InlineData("", "", "secret", false)]
    public void Validate_S3Settings_RequiresHttpServiceUrlAndCompleteKeyPair(
        string serviceUrl,
        string accessKey,
        string secretKey,
        bool valid
    )
    {
        StorageAccountSettings settings = new()
        {
            S3 = new()
            {
                ServiceUrl = serviceUrl,
                AccessKey = accessKey,
                SecretKey = secretKey,
            },
        };

        ValidateOptionsResult result = new StorageAccountSettingsValidator().Validate(
            null,
            settings
        );

        Assert.Equal(valid, result.Succeeded);
    }

    [Fact]
    public void Validate_S3Failure_DoesNotIncludeTheSecretKey()
    {
        ValidateOptionsResult result = new StorageAccountSettingsValidator().Validate(
            null,
            new StorageAccountSettings { S3 = new() { SecretKey = "do-not-log-me" } }
        );

        Assert.True(result.Failed);
        Assert.DoesNotContain("do-not-log-me", result.FailureMessage);
    }
}
