using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.Extensions.Configuration;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Tests;

public class AuditLogWriteQueueTests
{
    [Fact]
    public async Task QueueUsesConfiguredCapacityAndTracksEnqueues()
    {
        var queue = CreateQueue(new Dictionary<string, string?>
        {
            ["Security:AuditLogQueue:Capacity"] = "2",
            ["Security:AuditLogQueue:DeadLetterCapacity"] = "1"
        });

        await queue.WriteAsync(CreateJob("one"));
        Assert.True(queue.TryEnqueue(CreateJob("two")));
        Assert.False(queue.TryEnqueue(CreateJob("three")));

        var snapshot = queue.GetSnapshot();
        Assert.Equal(2, snapshot.Capacity);
        Assert.Equal(1, snapshot.DeadLetterCapacity);
        Assert.Equal(2, snapshot.EnqueuedCount);
    }

    [Fact]
    public void DeadLetterQueueIsBoundedAndTracksDrops()
    {
        var queue = CreateQueue(new Dictionary<string, string?>
        {
            ["Security:AuditLogQueue:DeadLetterCapacity"] = "1"
        });

        Assert.True(queue.TryEnqueueDeadLetter(CreateDeadLetter("one")));
        Assert.False(queue.TryEnqueueDeadLetter(CreateDeadLetter("two")));

        var snapshot = queue.GetSnapshot();
        Assert.Equal(1, snapshot.DeadLetteredCount);
        Assert.Equal(1, snapshot.DroppedDeadLetterCount);

        var drained = queue.DrainDeadLetters();
        Assert.Single(drained);
        Assert.Equal("action-one", drained[0].Job.Audit.Action);
    }

    private static AuditLogWriteQueue CreateQueue(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return new AuditLogWriteQueue(configuration);
    }

    private static AuditLogDeadLetterJob CreateDeadLetter(string id)
    {
        return new AuditLogDeadLetterJob(
            CreateJob(id),
            "PersistFailed",
            typeof(InvalidOperationException).FullName,
            "test",
            5,
            DateTime.UtcNow);
    }

    private static AuditLogWriteJob CreateJob(string id)
    {
        return new AuditLogWriteJob(
            TestApplicationFactory.TestTenantId,
            TestApplicationFactory.TestTenantSlug,
            new AuditLog
            {
                Id = $"audit-{id}",
                TenantId = TestApplicationFactory.TestTenantId,
                Action = $"action-{id}",
                Entity = "Test",
                EntityId = id,
                CreatedAt = DateTime.UtcNow
            });
    }
}
