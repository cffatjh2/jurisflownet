using System.Net;
using JurisFlow.Server.Services;
using Microsoft.Extensions.Configuration;

namespace JurisFlow.Server.Tests;

public class ForwardedHeadersConfigurationTests
{
    [Fact]
    public void CreateOptionsAddsConfiguredKnownProxyAndNetwork()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies:0"] = "10.1.2.3",
            ["ForwardedHeaders:KnownNetworks:0"] = "172.16.0.0/12",
            ["ForwardedHeaders:ForwardLimit"] = "2",
            ["ForwardedHeaders:RequireHeaderSymmetry"] = "true"
        });

        var options = ForwardedHeadersConfiguration.CreateOptions(configuration);

        Assert.Equal(2, options.ForwardLimit);
        Assert.True(options.RequireHeaderSymmetry);
        Assert.Contains(IPAddress.Parse("10.1.2.3"), options.KnownProxies);
        Assert.Contains(options.KnownIPNetworks, network => network.BaseAddress.Equals(IPAddress.Parse("172.16.0.0")));
    }

    [Fact]
    public void TrustAllProxiesClearsDefaultTrustBoundaryOnlyWhenExplicitlyConfigured()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:TrustAllProxies"] = "true",
            ["ForwardedHeaders:AllowTrustAllProxiesInProduction"] = "true"
        });

        var options = ForwardedHeadersConfiguration.CreateOptions(configuration);

        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
        Assert.True(ForwardedHeadersConfiguration.HasExplicitTrustBoundary(configuration));
        Assert.True(ForwardedHeadersConfiguration.IsTrustAllAllowedInProduction(configuration));
    }

    [Fact]
    public void ProductionTrustBoundaryCheckFailsWhenEnabledWithoutExplicitBoundary()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:Enabled"] = "true"
        });

        Assert.False(ForwardedHeadersConfiguration.HasExplicitTrustBoundary(configuration));
    }

    [Fact]
    public void InvalidKnownNetworkThrows()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownNetworks:0"] = "172.16.0.0/99"
        });

        var ex = Assert.Throws<InvalidOperationException>(() => ForwardedHeadersConfiguration.CreateOptions(configuration));
        Assert.Contains("invalid prefix length", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
