using Task = System.Threading.Tasks.Task;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace JurisFlow.Server.Services
{
    public class HolidaySeedService
    {
        private readonly ILogger<HolidaySeedService> _logger;
        private static readonly string[] StateCodes = new[]
        {
            "AL", "AK", "AZ", "AR", "CA", "CO", "CT", "DE", "FL", "GA",
            "HI", "ID", "IL", "IN", "IA", "KS", "KY", "LA", "ME", "MD",
            "MA", "MI", "MN", "MS", "MO", "MT", "NE", "NV", "NH", "NJ",
            "NM", "NY", "NC", "ND", "OH", "OK", "OR", "PA", "RI", "SC",
            "SD", "TN", "TX", "UT", "VT", "VA", "WA", "WV", "WI", "WY",
            "DC"
        };

        public HolidaySeedService(ILogger<HolidaySeedService> logger)
        {
            _logger = logger;
        }

        public async Task EnsureSeededAsync(JurisFlowDbContext context, int startYear, int yearsAhead)
        {
            var targetYears = Enumerable.Range(startYear, Math.Max(1, yearsAhead + 1)).ToList();
            var jurisdictions = new List<string> { "US-Federal" };
            jurisdictions.AddRange(StateCodes.Select(code => $"US-{code}"));

            var existingTargets = await context.Holidays
                .AsNoTracking()
                .Where(h => h.Jurisdiction != null && targetYears.Contains(h.Date.Year) && jurisdictions.Contains(h.Jurisdiction))
                .Select(h => new
                {
                    Jurisdiction = h.Jurisdiction!,
                    Year = h.Date.Year
                })
                .Distinct()
                .ToListAsync();

            var existingTargetSet = existingTargets
                .Select(item => BuildTargetKey(item.Jurisdiction, item.Year))
                .ToHashSet(StringComparer.Ordinal);

            var holidaysToSeed = new List<Holiday>();
            var seededTargets = new List<string>();
            foreach (var year in targetYears)
            {
                foreach (var jurisdiction in jurisdictions)
                {
                    var targetKey = BuildTargetKey(jurisdiction, year);
                    if (existingTargetSet.Contains(targetKey))
                    {
                        continue;
                    }

                    var holidays = BuildHolidays(year, jurisdiction);
                    if (holidays.Count == 0)
                    {
                        continue;
                    }

                    holidaysToSeed.AddRange(holidays);
                    existingTargetSet.Add(targetKey);
                    seededTargets.Add(targetKey);
                }
            }

            if (holidaysToSeed.Count == 0)
            {
                return;
            }

            context.Holidays.AddRange(holidaysToSeed);
            await context.SaveChangesAsync();
            _logger.LogInformation(
                "Seeded {HolidayCount} holidays across {TargetCount} jurisdiction-year targets.",
                holidaysToSeed.Count,
                seededTargets.Count);
        }

        private static List<Holiday> BuildHolidays(int year, string jurisdiction)
        {
            var holidays = new List<Holiday>();
            var federal = BuildFederalHolidays(year);

            if (string.Equals(jurisdiction, "US-Federal", StringComparison.OrdinalIgnoreCase))
            {
                holidays.AddRange(federal);
                return holidays;
            }

            holidays.AddRange(federal.Select(h => CloneWithJurisdiction(h, jurisdiction)));
            holidays.AddRange(BuildStateHolidays(year, jurisdiction));
            return holidays;
        }

        private static Holiday CloneWithJurisdiction(Holiday source, string jurisdiction)
        {
            return new Holiday
            {
                Id = Guid.NewGuid().ToString(),
                Date = NormalizeUtcDate(source.Date),
                Name = source.Name,
                Jurisdiction = jurisdiction,
                IsCourtHoliday = source.IsCourtHoliday,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }

        private static List<Holiday> BuildFederalHolidays(int year)
        {
            var holidays = new List<Holiday>();
            void AddObserved(DateTime date, string name)
            {
                var observed = ObserveWeekend(date);
                holidays.Add(new Holiday
                {
                    Id = Guid.NewGuid().ToString(),
                    Date = NormalizeUtcDate(observed),
                    Name = name + (observed.Date != date.Date ? " (Observed)" : string.Empty),
                    Jurisdiction = "US-Federal",
                    IsCourtHoliday = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            AddObserved(UtcDate(year, 1, 1), "New Year's Day");
            holidays.Add(CreateHoliday(NthWeekday(year, 1, DayOfWeek.Monday, 3), "Martin Luther King Jr. Day", "US-Federal"));
            holidays.Add(CreateHoliday(NthWeekday(year, 2, DayOfWeek.Monday, 3), "Presidents' Day", "US-Federal"));
            holidays.Add(CreateHoliday(LastWeekday(year, 5, DayOfWeek.Monday), "Memorial Day", "US-Federal"));
            AddObserved(UtcDate(year, 6, 19), "Juneteenth");
            AddObserved(UtcDate(year, 7, 4), "Independence Day");
            holidays.Add(CreateHoliday(NthWeekday(year, 9, DayOfWeek.Monday, 1), "Labor Day", "US-Federal"));
            holidays.Add(CreateHoliday(NthWeekday(year, 10, DayOfWeek.Monday, 2), "Columbus Day", "US-Federal"));
            AddObserved(UtcDate(year, 11, 11), "Veterans Day");
            holidays.Add(CreateHoliday(NthWeekday(year, 11, DayOfWeek.Thursday, 4), "Thanksgiving Day", "US-Federal"));
            AddObserved(UtcDate(year, 12, 25), "Christmas Day");

            return holidays;
        }

        private static List<Holiday> BuildStateHolidays(int year, string jurisdiction)
        {
            var items = new List<Holiday>();
            if (string.Equals(jurisdiction, "US-CA", StringComparison.OrdinalIgnoreCase))
            {
                items.Add(CreateHoliday(UtcDate(year, 3, 31), "Cesar Chavez Day", jurisdiction));
                items.Add(CreateHoliday(UtcDate(year, 9, 9), "California Admission Day", jurisdiction));
            }
            else if (string.Equals(jurisdiction, "US-NY", StringComparison.OrdinalIgnoreCase))
            {
                items.Add(CreateHoliday(FirstTuesdayAfterFirstMonday(year, 11), "Election Day", jurisdiction));
            }
            else if (string.Equals(jurisdiction, "US-TX", StringComparison.OrdinalIgnoreCase))
            {
                items.Add(CreateHoliday(UtcDate(year, 3, 2), "Texas Independence Day", jurisdiction));
                items.Add(CreateHoliday(UtcDate(year, 4, 21), "San Jacinto Day", jurisdiction));
            }
            else if (string.Equals(jurisdiction, "US-FL", StringComparison.OrdinalIgnoreCase))
            {
                items.Add(CreateHoliday(UtcDate(year, 11, 11), "Florida Veterans Day (State)", jurisdiction));
            }

            return items;
        }

        private static Holiday CreateHoliday(DateTime date, string name, string jurisdiction)
        {
            return new Holiday
            {
                Id = Guid.NewGuid().ToString(),
                Date = NormalizeUtcDate(date),
                Name = name,
                Jurisdiction = jurisdiction,
                IsCourtHoliday = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }

        private static string BuildTargetKey(string jurisdiction, int year)
        {
            return $"{jurisdiction}:{year}";
        }

        private static DateTime ObserveWeekend(DateTime date)
        {
            return date.DayOfWeek switch
            {
                DayOfWeek.Saturday => date.AddDays(-1),
                DayOfWeek.Sunday => date.AddDays(1),
                _ => date
            };
        }

        private static DateTime NthWeekday(int year, int month, DayOfWeek day, int nth)
        {
            var first = UtcDate(year, month, 1);
            var offset = ((int)day - (int)first.DayOfWeek + 7) % 7;
            return first.AddDays(offset + (nth - 1) * 7);
        }

        private static DateTime LastWeekday(int year, int month, DayOfWeek day)
        {
            var last = UtcDate(year, month, DateTime.DaysInMonth(year, month));
            var offset = ((int)last.DayOfWeek - (int)day + 7) % 7;
            return last.AddDays(-offset);
        }

        private static DateTime FirstTuesdayAfterFirstMonday(int year, int month)
        {
            var firstMonday = NthWeekday(year, month, DayOfWeek.Monday, 1);
            return firstMonday.AddDays(1);
        }

        private static DateTime UtcDate(int year, int month, int day)
        {
            return new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
        }

        private static DateTime NormalizeUtcDate(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc)
            {
                return value.Date;
            }

            return DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
        }
    }
}
