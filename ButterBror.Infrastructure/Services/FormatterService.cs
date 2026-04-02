
using ButterBror.Core.Interfaces;

namespace ButterBror.Infrastructure.Services;

public class FormatterService : IFormatterService
{
    private readonly ILocalizationService _localizationService;

    public FormatterService(ILocalizationService localizationService)
    {
        _localizationService = localizationService;
    }

    public async Task<string> FormatTimeSpanAsync(TimeSpan ts, string locale)
    {
        if (ts.TotalDays >= 1)
        {
            return $"{ts.Days} {await GetL(locale, "day")} " +
                   $"{ts.Hours} {await GetL(locale, "hour")} " +
                   $"{ts.Minutes} {await GetL(locale, "minute")}";
        }
        
        if (ts.TotalHours >= 1)
        {
            return $"{ts.Hours} {await GetL(locale, "hour")} " +
                   $"{ts.Minutes} {await GetL(locale, "minute")} " +
                   $"{ts.Seconds} {await GetL(locale, "second")}";
        }

        if (ts.TotalMinutes >= 1)
        {
            return $"{ts.Minutes} {await GetL(locale, "minute")} " +
                   $"{ts.Seconds} {await GetL(locale, "second")}";
        }

        return $"{ts.Seconds} {await GetL(locale, "second")}";
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
            return $"{years} {await GetL(locale, "year")} {months} {await GetL(locale, "month")} {days} {await GetL(locale, "day")}";
        
        if (months > 0)
            return $"{months} {await GetL(locale, "month")} {days} {await GetL(locale, "day")} {timeDiff.Hours} {await GetL(locale, "hour")}";

        if (days > 0)
            return $"{days} {await GetL(locale, "day")} {timeDiff.Hours} {await GetL(locale, "hour")} {timeDiff.Minutes} {await GetL(locale, "minute")}";
        
        return await FormatTimeSpanAsync(timeDiff, locale);
    }

    private async Task<string> GetL(string locale, string unit)
    {
        return await _localizationService.GetStringAsync($"word.{unit}", locale);
    }
}