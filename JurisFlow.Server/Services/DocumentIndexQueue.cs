using System.Threading.Channels;
using JurisFlow.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace JurisFlow.Server.Services
{
    public sealed record DocumentIndexJob(
        string TenantId,
        string? TenantSlug,
        string DocumentId);

    public sealed class DocumentIndexQueue
    {
        private readonly Channel<DocumentIndexJob> _channel;

        public DocumentIndexQueue(IConfiguration configuration)
        {
            var capacity = Math.Clamp(configuration.GetValue("Documents:IndexQueue:Capacity", 512), 1, 10_000);
            _channel = Channel.CreateBounded<DocumentIndexJob>(new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
        }

        public bool TryEnqueue(DocumentIndexJob job) => _channel.Writer.TryWrite(job);

        public ValueTask EnqueueAsync(DocumentIndexJob job, CancellationToken cancellationToken = default) =>
            _channel.Writer.WriteAsync(job, cancellationToken);

        public IAsyncEnumerable<DocumentIndexJob> DequeueAllAsync(CancellationToken cancellationToken) =>
            _channel.Reader.ReadAllAsync(cancellationToken);
    }

    public sealed class DocumentIndexHostedService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly DocumentIndexQueue _queue;
        private readonly ILogger<DocumentIndexHostedService> _logger;

        public DocumentIndexHostedService(
            IServiceProvider serviceProvider,
            DocumentIndexQueue queue,
            ILogger<DocumentIndexHostedService> logger)
        {
            _serviceProvider = serviceProvider;
            _queue = queue;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var job in _queue.DequeueAllAsync(stoppingToken))
            {
                using var scope = _serviceProvider.CreateScope();
                var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
                tenantContext.Set(job.TenantId, job.TenantSlug);

                var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
                var indexService = scope.ServiceProvider.GetRequiredService<DocumentIndexService>();

                try
                {
                    var document = await db.Documents
                        .FirstOrDefaultAsync(d => d.Id == job.DocumentId, stoppingToken);
                    if (document == null)
                    {
                        continue;
                    }

                    await indexService.UpsertIndexAsync(document);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Document indexing failed in background for {DocumentId}.", job.DocumentId);
                }
            }
        }
    }
}
