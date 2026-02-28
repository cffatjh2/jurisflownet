using System.Net;
using System.Text;
using System.Text.Json;
using Task = System.Threading.Tasks.Task;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JurisFlow.Server.Tests;

public class DeadlineCalculationTests : IClassFixture<TestApplicationFactory>
{
    private readonly TestApplicationFactory _factory;

    public DeadlineCalculationTests(TestApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CourtDaysSkipFederalHoliday()
    {
        var ruleId = SeedCourtRule(
            name: "Court Days Before Filing",
            jurisdiction: "US-CA",
            dayType: "Court",
            direction: "Before",
            daysCount: 2,
            extendIfWeekend: true);

        SeedHoliday(new DateTime(2024, 11, 28), "Thanksgiving Day", "US-Federal");

        var payload = new
        {
            courtRuleId = ruleId,
            triggerDate = "2024-11-29",
            serviceMethod = "Personal"
        };

        var response = await PostCalculate(payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dueDate = await ReadDueDate(response);
        Assert.Equal(new DateTime(2024, 11, 26), dueDate);
    }

    [Fact]
    public async Task CalendarDaysExtendAfterHoliday()
    {
        var ruleId = SeedCourtRule(
            name: "Calendar Days After Service",
            jurisdiction: "US-NY",
            dayType: "Calendar",
            direction: "After",
            daysCount: 1,
            extendIfWeekend: true);

        SeedHoliday(new DateTime(2024, 12, 25), "Christmas Day", "US-Federal");

        var payload = new
        {
            courtRuleId = ruleId,
            triggerDate = "2024-12-24",
            serviceMethod = "Personal"
        };

        var response = await PostCalculate(payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dueDate = await ReadDueDate(response);
        Assert.Equal(new DateTime(2024, 12, 26), dueDate);
    }

    [Fact]
    public async Task CaliforniaCourtDaysSkipThanksgiving()
    {
        var ruleId = SeedCourtRule(
            name: "Reply Due Before Hearing",
            jurisdiction: "US-CA",
            dayType: "Court",
            direction: "Before",
            daysCount: 5,
            extendIfWeekend: true);

        SeedHoliday(new DateTime(2024, 11, 28), "Thanksgiving Day", "US-Federal");

        var payload = new
        {
            courtRuleId = ruleId,
            triggerDate = "2024-12-02",
            serviceMethod = "Personal"
        };

        var response = await PostCalculate(payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dueDate = await ReadDueDate(response);
        Assert.Equal(new DateTime(2024, 11, 22), dueDate);
    }

    [Fact]
    public async Task NewYorkAnswerExtendsAfterHoliday()
    {
        var ruleId = SeedCourtRule(
            name: "Answer After Service",
            jurisdiction: "US-NY",
            dayType: "Calendar",
            direction: "After",
            daysCount: 20,
            extendIfWeekend: true);

        SeedHoliday(new DateTime(2024, 7, 4), "Independence Day", "US-Federal");

        var payload = new
        {
            courtRuleId = ruleId,
            triggerDate = "2024-06-14",
            serviceMethod = "Personal"
        };

        var response = await PostCalculate(payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dueDate = await ReadDueDate(response);
        Assert.Equal(new DateTime(2024, 7, 5), dueDate);
    }

    private string SeedCourtRule(string name, string jurisdiction, string dayType, string direction, int daysCount, bool extendIfWeekend, int serviceDaysAdd = 0)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);
        db.Database.EnsureCreated();

        var rule = new CourtRule
        {
            Name = name,
            RuleType = "State",
            Jurisdiction = jurisdiction,
            TriggerEvent = "Service",
            DaysCount = daysCount,
            DayType = dayType,
            Direction = direction,
            ExtendIfWeekend = extendIfWeekend,
            ServiceDaysAdd = serviceDaysAdd
        };

        db.CourtRules.Add(rule);
        db.SaveChanges();
        return rule.Id;
    }

    private void SeedHoliday(DateTime date, string name, string jurisdiction)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);
        db.Database.EnsureCreated();

        db.Holidays.Add(new Holiday
        {
            Id = Guid.NewGuid().ToString(),
            Date = date,
            Name = name,
            Jurisdiction = jurisdiction,
            IsCourtHoliday = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    private async Task<HttpResponseMessage> PostCalculate(object payload)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/deadlines/calculate");
        request.Headers.Add("X-Test-UserId", "user-1");
        request.Headers.Add("X-Test-Role", "Partner");
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        return await client.SendAsync(request);
    }

    private static async Task<DateTime> ReadDueDate(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var dueDateString = doc.RootElement.GetProperty("dueDate").GetString();
        return DateTime.Parse(dueDateString!);
    }
}
