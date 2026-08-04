
using ButterBror.Core.Interfaces;

namespace ButterBror.Infrastructure.Services;

public class FormatterService(ILocalizationService localizationService) : IFormatterService
{
    public async Task<string> FormatTimeSpanAsync(TimeSpan ts, string locale)
    {
        ts = ts.Duration();

        List<(int Value, string Unit)> candidates;

        if (ts.TotalDays >= 1)
            candidates = [(ts.Days, "day"), (ts.Hours, "hour"), (ts.Minutes, "minute")];
        else if (ts.TotalHours >= 1)
            candidates = [(ts.Hours, "hour"), (ts.Minutes, "minute"), (ts.Seconds, "second")];
        else if (ts.TotalMinutes >= 1)
            candidates = [(ts.Minutes, "minute"), (ts.Seconds, "second")];
        else
            candidates = [(ts.Seconds, "second")];

        return await BuildFormattedStringAsync(candidates, locale);
    }

    public async Task<string> FormatUtcDateAsync(DateTime utcDate, string locale)
    {
        return await FormatPreciseAsync(DateTime.UtcNow, utcDate, locale);
    }

    public async Task<string> FormatLocalDateAsync(DateTime localDate, string locale)
    {
        return await FormatPreciseAsync(DateTime.Now, localDate, locale);
    }

    public async Task<string> FormatRegionalDateAsync(DateTime remoteDate, string timeZoneId, string locale)
    {
        TimeZoneInfo targetZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        DateTime nowInTargetZone = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, targetZone);
        return await FormatPreciseAsync(nowInTargetZone, remoteDate, locale);
    }

    private async Task<string> FormatPreciseAsync(DateTime start, DateTime end, string locale)
    {
        if (end < start) (start, end) = (end, start);

        int years = end.Year - start.Year;
        int months = end.Month - start.Month;
        int days = end.Day - start.Day;

        if (days < 0)
        {
            months--;
            days += DateTime.DaysInMonth(end.AddMonths(-1).Year, end.AddMonths(-1).Month);
        }

        if (months < 0)
        {
            years--;
            months += 12;
        }

        TimeSpan timeDiff = end.TimeOfDay - start.TimeOfDay;
        if (timeDiff.Ticks < 0)
        {
            days--;
            timeDiff = timeDiff.Add(TimeSpan.FromDays(1));
            
            if (days < 0)
            {
                months--;
                days += DateTime.DaysInMonth(end.AddMonths(-1).Year, end.AddMonths(-1).Month);
                if (months < 0) { years--; months += 12; }
            }
        }

        if (years > 0)
        {
            return await BuildFormattedStringAsync([
                (years, "year"),
                (months, "month"),
                (days, "day")
            ], locale);
        }

        if (months > 0)
        {
            return await BuildFormattedStringAsync([
                (months, "month"),
                (days, "day"),
                (timeDiff.Hours, "hour")
            ], locale);
        }

        if (days > 0)
        {
            return await BuildFormattedStringAsync([
                (days, "day"),
                (timeDiff.Hours, "hour"),
                (timeDiff.Minutes, "minute")
            ], locale);
        }
        
        return await FormatTimeSpanAsync(timeDiff, locale);
    }

    private async Task<string> BuildFormattedStringAsync(IEnumerable<(int Value, string Unit)> parts, string locale)
    {
        var valueTuples = parts.ToList();
        var activeParts = valueTuples.Where(p => p.Value > 0).ToList();
        
        if (activeParts.Count == 0)
        {
            var fallbackUnit = valueTuples.LastOrDefault().Unit ?? "second";
            return $"0 {await GetL(locale, fallbackUnit)}";
        }
        
        var tasks = activeParts.Select(async p =>
            $"{p.Value} {await GetL(locale, p.Unit)}");

        var results = await Task.WhenAll(tasks);
        return string.Join(" ", results);
    }
    
    private async Task<string> GetL(string locale, string unit)
    {
        return await localizationService.GetStringAsync($"word.{unit}", locale);
    }
}