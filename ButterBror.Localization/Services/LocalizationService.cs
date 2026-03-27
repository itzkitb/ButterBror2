using System.Collections.Concurrent;
using System.Globalization;
using ButterBror.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ButterBror.Localization.Services;

/// <summary>
/// Main localization service
/// </summary>
public class LocalizationService : ILocalizationService
{
    private readonly LocaleRegistryService _registry;
    private readonly TranslationFileLoader _fileLoader;
    private readonly ILogger<LocalizationService> _logger;

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _translationCache
        = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _moduleDefaultsCache
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
            return key;

        var resolvedLocale = _registry.ResolveLocale(locale) ?? _defaultLocale ?? "EN_US";
        
        // S0: Try cache
        if (TryGetFromCache(resolvedLocale, key, args, out var cached))
            return cached;

        // S1: Load from file
        var result = await LoadAndFormatStringAsync(resolvedLocale, key, args);
        
        // S2: Fallback chain
        if (result == key && resolvedLocale != _defaultLocale)
        {
            _logger.LogDebug("Fallback: key '{Key}' not found in {Locale}, trying {Default}", 
                key, resolvedLocale, _defaultLocale);
            result = await LoadAndFormatStringAsync(_defaultLocale!, key, args);
        }

        // S3: Final fallback
        if (result == key && args?.Length > 0)
        {
            try
            {
                result = string.Format(CultureInfo.InvariantCulture, key, args);
            }
            catch
            {
                //
            }
        }

        // S4: Cache the result
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

    public void RegisterModuleTranslations(
        string moduleId,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> translations)
    {
        if (translations == null || translations.Count == 0)
        {
            _logger.LogDebug("No translations to register for module: {ModuleId}", moduleId);
            return;
        }

        foreach (var (locale, localeTranslations) in translations)
        {
            var localeCache = _moduleDefaultsCache.GetOrAdd(locale, _ => new ConcurrentDictionary<string, string>());
            foreach (var (key, value) in localeTranslations)
            {
                localeCache.TryAdd(key, value);
            }
        }

        _logger.LogDebug(
            "Registered {Count} translation(s) for module {ModuleId}",
            translations.Values.Sum(v => v.Count),
            moduleId);
    }

    private bool TryGetFromCache(string locale, string key, object[] args, out string result)
    {
        result = key;

        // S0: File-based cache
        if (_translationCache.TryGetValue(locale, out var fileLocaleCache))
        {
            if (fileLocaleCache.TryGetValue(key, out var fileTemplate))
            {
                try
                {
                    result = args?.Length > 0
                        ? string.Format(CultureInfo.InvariantCulture, fileTemplate, args)
                        : fileTemplate;
                    return true;
                }
                catch (FormatException ex)
                {
                    _logger.LogWarning(ex, "Failed to format key '{Key}' in locale {Locale}", key, locale);
                }
            }
        }

        // S1: Module defaults cache
        if (_moduleDefaultsCache.TryGetValue(locale, out var moduleLocaleCache))
        {
            if (moduleLocaleCache.TryGetValue(key, out var moduleTemplate))
            {
                try
                {
                    result = args?.Length > 0
                        ? string.Format(CultureInfo.InvariantCulture, moduleTemplate, args)
                        : moduleTemplate;
                    return true;
                }
                catch (FormatException ex)
                {
                    _logger.LogWarning(ex, "Failed to format key '{Key}' from module defaults in locale {Locale}", key, locale);
                }
            }
        }

        return false;
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
        if (translation?.Strings.TryGetValue(key, out var template) != true || template == null)
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
            return template;
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