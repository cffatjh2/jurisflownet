using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Tests;

public class IntegrationConnectorRegistryTests
{
    [Fact]
    public void Resolve_ReturnsSpecificConnector_WhenProviderHasDedicatedConnector()
    {
        var quickBooks = new StubConnector("quickbooks", key =>
            string.Equals(key, IntegrationProviderKeys.QuickBooksOnline, StringComparison.OrdinalIgnoreCase));
        var fallback = new StubConnector("legacy", _ => true);
        var registry = new IntegrationConnectorRegistry(new IIntegrationConnector[] { quickBooks, fallback });

        var connector = registry.Resolve(IntegrationProviderKeys.QuickBooksOnline);

        Assert.NotNull(connector);
        Assert.Same(quickBooks, connector);
    }

    [Fact]
    public void Resolve_ReturnsLegacyFallback_WhenNoSpecificConnectorExists()
    {
        var quickBooks = new StubConnector("quickbooks", key =>
            string.Equals(key, IntegrationProviderKeys.QuickBooksOnline, StringComparison.OrdinalIgnoreCase));
        var fallback = new StubConnector("legacy", _ => true);
        var registry = new IntegrationConnectorRegistry(new IIntegrationConnector[] { quickBooks, fallback });

        var connector = registry.Resolve("courtlistener-dockets");

        Assert.NotNull(connector);
        Assert.Same(fallback, connector);
    }

    [Fact]
    public void Resolve_ReturnsNull_WhenProviderKeyMissing()
    {
        var registry = new IntegrationConnectorRegistry(Array.Empty<IIntegrationConnector>());

        var connector = registry.Resolve(" ");

        Assert.Null(connector);
    }

    private sealed class StubConnector : IIntegrationConnector
    {
        private readonly Func<string, bool> _canHandle;

        public StubConnector(string name, Func<string, bool> canHandle)
        {
            Name = name;
            _canHandle = canHandle;
        }

        public string Name { get; }

        public bool CanHandle(string providerKey)
        {
            return _canHandle(providerKey);
        }

        public Task<IntegrationSyncResult> SyncAsync(IntegrationConnection connection, CancellationToken cancellationToken)
        {
            return Task.FromResult(new IntegrationSyncResult { Success = true });
        }
    }
}
