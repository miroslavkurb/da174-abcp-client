using ABCPClient.Application.Configuration;
using ABCPClient.Application.DependencyInjection;
using ABCPClient.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ABCPClient.Tests;

/// <summary>
/// Проверяет, что корень композиции собирается и настройки привязываются к конфигурации.
/// </summary>
public sealed class CompositionRootTests
{
    private static ServiceProvider BuildProvider(Dictionary<string, string?>? settings = null)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings ?? [])
            .Build();

        return new ServiceCollection()
            .AddApplicationLayer()
            .AddInfrastructureLayer(configuration)
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
    }

    [Fact]
    public void Container_builds_and_validates()
    {
        using ServiceProvider provider = BuildProvider();

        Assert.NotNull(provider.GetRequiredService<IHttpClientFactory>());
    }

    [Fact]
    public void Api_options_are_bound_from_configuration()
    {
        using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Abcp:BaseUrl"] = "https://demo.public.api.abcp.ru",
            ["Abcp:Login"] = "api-admin",
            ["Abcp:PasswordMd5"] = "0123456789abcdef0123456789abcdef",
            ["Abcp:TimeoutSeconds"] = "45",
        });

        AbcpApiOptions options = provider.GetRequiredService<IOptions<AbcpApiOptions>>().Value;

        Assert.Equal("https://demo.public.api.abcp.ru", options.BaseUrl);
        Assert.Equal(45, options.TimeoutSeconds);
        Assert.True(options.IsConfigured);
    }

    [Fact]
    public void Api_options_are_not_configured_when_credentials_are_missing()
    {
        using ServiceProvider provider = BuildProvider();

        AbcpApiOptions options = provider.GetRequiredService<IOptions<AbcpApiOptions>>().Value;

        Assert.False(options.IsConfigured);
    }

    [Fact]
    public void Sync_options_fall_back_to_defaults()
    {
        using ServiceProvider provider = BuildProvider();

        SyncOptions options = provider.GetRequiredService<IOptions<SyncOptions>>().Value;

        Assert.True(options.Enabled);
        Assert.Equal(120, options.PollingIntervalSeconds);
        Assert.Equal(5, options.OverlapMinutes);
    }
}
