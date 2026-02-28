using JurisFlow.Server.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace JurisFlow.Server.Tests;

public class LoginAttemptServiceTests
{
    [Fact]
    public void LocksOutAfterConfiguredAccountFailures()
    {
        var service = CreateService(new Dictionary<string, string?>
        {
            ["Security:LoginFailureLimitPerAccount"] = "3",
            ["Security:LoginFailureLimitPerIp"] = "20",
            ["Security:LoginFailureWindowMinutes"] = "15",
            ["Security:LoginLockoutMinutes"] = "10"
        });

        var tenantId = "tenant-1";
        var email = "attorney@example.com";
        var ip = "203.0.113.10";

        var attempt1 = service.RegisterFailure(tenantId, email, ip);
        var attempt2 = service.RegisterFailure(tenantId, email, ip);
        var attempt3 = service.RegisterFailure(tenantId, email, ip);

        Assert.False(attempt1.IsLockedOut);
        Assert.False(attempt2.IsLockedOut);
        Assert.True(attempt3.IsLockedOut);
        Assert.NotNull(attempt3.RetryAfter);
        Assert.True(attempt3.RetryAfter!.Value.TotalSeconds > 0);
    }

    [Fact]
    public void LocksOutByIpAcrossDifferentSubjects()
    {
        var service = CreateService(new Dictionary<string, string?>
        {
            ["Security:LoginFailureLimitPerAccount"] = "10",
            ["Security:LoginFailureLimitPerIp"] = "5",
            ["Security:LoginFailureWindowMinutes"] = "15",
            ["Security:LoginLockoutMinutes"] = "10"
        });

        var tenantId = "tenant-2";
        var ip = "198.51.100.27";

        var first = service.RegisterFailure(tenantId, "a@example.com", ip);
        var second = service.RegisterFailure(tenantId, "b@example.com", ip);
        var third = service.RegisterFailure(tenantId, "c@example.com", ip);
        var fourth = service.RegisterFailure(tenantId, "d@example.com", ip);
        var fifth = service.RegisterFailure(tenantId, "e@example.com", ip);
        var statusAfterLock = service.GetStatus(tenantId, "f@example.com", ip);

        Assert.False(first.IsLockedOut);
        Assert.False(second.IsLockedOut);
        Assert.False(third.IsLockedOut);
        Assert.False(fourth.IsLockedOut);
        Assert.True(fifth.IsLockedOut);
        Assert.True(statusAfterLock.IsLockedOut);
    }

    [Fact]
    public void SuccessClearsFailureState()
    {
        var service = CreateService(new Dictionary<string, string?>
        {
            ["Security:LoginFailureLimitPerAccount"] = "4",
            ["Security:LoginFailureLimitPerIp"] = "20",
            ["Security:LoginFailureWindowMinutes"] = "15",
            ["Security:LoginLockoutMinutes"] = "10"
        });

        var tenantId = "tenant-3";
        var email = "partner@example.com";
        var ip = "192.0.2.45";

        service.RegisterFailure(tenantId, email, ip);
        service.RegisterFailure(tenantId, email, ip);

        service.RegisterSuccess(tenantId, email, ip);

        var statusAfterSuccess = service.GetStatus(tenantId, email, ip);
        Assert.False(statusAfterSuccess.IsLockedOut);
        Assert.Equal(4, statusAfterSuccess.RemainingAttempts);
    }

    private static LoginAttemptService CreateService(Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Security:LoginFailureLimitPerAccount"] = "5",
            ["Security:LoginFailureLimitPerIp"] = "20",
            ["Security:LoginFailureWindowMinutes"] = "15",
            ["Security:LoginLockoutMinutes"] = "15"
        };

        foreach (var item in overrides)
        {
            values[item.Key] = item.Value;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var cache = new MemoryCache(new MemoryCacheOptions());
        return new LoginAttemptService(cache, configuration, NullLogger<LoginAttemptService>.Instance);
    }
}
