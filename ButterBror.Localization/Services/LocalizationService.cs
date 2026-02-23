using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ButterBror.Localization.Interfaces;
using ButterBror.Localization.Models;
using Microsoft.Extensions.Logging;

namespace ButterBror.Localization.Services;

/// <summary>
/// Main localization service with caching and fallback support
/// </summary>
public class LocalizationService : ILocalizationService
{
    private readonly LocaleRegistryService _registry;
    private readonly TranslationFileLoader _fileLoader;
    private readonly ILogger<LocalizationService> _logger;
    
    // Cache: localeCode -> (key -> formatted string template)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _translationCache 
        = new(StringComparer.OrdinalIgnoreCase);
    
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private string? _defaultLocale;

    public LocalizationService(
        LocaleRegistryService registry,
        TranslationFileLoader fileLoader,
        ILogger<LocalizationService> logger)
    {
        _registry = registry;
        _fileLoader = fileLoader;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _registry.InitializeAsync(ct);
        _defaultLocale = _registry.GetDefaultLocale();
        await LoadAllTranslationsAsync(ct);
    }

    public async Task<string> GetStringAsync(
        string key, 
        string locale, 
        params object[] args)
    {
        if (string.IsNullOrWhiteSpace(key))
            return key; // Fallback: return key itself

        var resolvedLocale = _registry.ResolveLocale(locale) ?? _defaultLocale ?? "EN_US";
        
        // Try cache first
        if (TryGetFromCache(resolvedLocale, key, args, out var cached))
            return cached;

        // Load from file if not cached
        var result = await LoadAndFormatStringAsync(resolvedLocale, key, args);
        
        // Fallback chain
        if (result == key && resolvedLocale != _defaultLocale)
        {
            _logger.LogDebug("Fallback: key '{Key}' not found in {Locale}, trying {Default}", 
                key, resolvedLocale, _defaultLocale);
            result = await LoadAndFormatStringAsync(_defaultLocale!, key, args);
        }

        // Final fallback: return formatted key
        if (result == key && args?.Length > 0)
        {
            try
            {
                result = string.Format(CultureInfo.InvariantCulture, key, args);
            }
            catch
            {
                // Ignore formatting errors in fallback
            }
        }

        // Cache the result (even if it's a fallback)
        CacheString(resolvedLocale, key, result);

        return result;
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            _translationCache.Clear();
            await _registry.ReloadAsync(cancellationToken);
            _defaultLocale = _registry.GetDefaultLocale();
            await LoadAllTranslationsAsync(cancellationToken);
            _logger.LogInformation("Localization cache reloaded");
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public bool IsLocaleRegistered(string locale) => _registry.IsLocaleRegistered(locale);
    public string ResolveLocale(string locale) => _registry.ResolveLocale(locale) ?? _defaultLocale ?? "EN_US";

    private bool TryGetFromCache(string locale, string key, object[] args, out string result)
    {
        result = key;
        if (!_translationCache.TryGetValue(locale, out var localeCache))
            return false;
        
        if (!localeCache.TryGetValue(key, out var template))
            return false;

        try
        {
            result = args?.Length > 0 
                ? string.Format(CultureInfo.InvariantCulture, template, args) 
                : template;
            return true;
        }
        catch (FormatException ex)
        {
            _logger.LogWarning(ex, "Failed to format key '{Key}' in locale {Locale}", key, locale);
            return false;
        }
    }

    private void CacheString(string locale, string key, string value)
    {
        var localeCache = _translationCache.GetOrAdd(locale, _ => new ConcurrentDictionary<string, string>());
        localeCache[key] = value;
    }

    private async Task<string> LoadAndFormatStringAsync(string locale, string key, object[] args)
    {
        var metadata = _registry.GetLocaleMetadata(locale);
        if (metadata?.FilePath == null)
            return key;

        var translation = await _fileLoader.LoadTranslationAsync(metadata.FilePath);
        if (translation?.Strings.TryGetValue(key, out var template) != true)
            return key;

        CacheString(locale, key, template);

        try
        {
            return args?.Length > 0 
                ? string.Format(CultureInfo.InvariantCulture, template, args) 
                : template;
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex, "Failed to format translation key '{Key}' in locale {Locale}", key, locale);
            return template; // Return unformatted template as fallback
        }
    }

    private async Task LoadAllTranslationsAsync(CancellationToken ct)
    {
        foreach (var localeCode in _registry.GetAllLocales())
        {
            var metadata = _registry.GetLocaleMetadata(localeCode);
            if (metadata?.FilePath == null)
                continue;

            var translation = await _fileLoader.LoadTranslationAsync(metadata.FilePath, ct);
            if (translation?.Strings != null)
            {
                var localeCache = _translationCache.GetOrAdd(localeCode, _ => new ConcurrentDictionary<string, string>());
                foreach (var (key, value) in translation.Strings)
                {
                    localeCache[key] = value;
                }
            }
        }
    }
}