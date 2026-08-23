using System.Collections.Concurrent;
using System.Globalization;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Scopes;
using Microsoft.Extensions.Logging;

namespace ButterBror.Localization.Services;

/// <summary>
/// Main localization service
/// </summary>
public class LocalizationService(
    LocaleRegistryService registry,
    TranslationFileLoader fileLoader,
    ILogger<LocalizationService> logger)
    : ILocalizationService
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _translationCache
        = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _moduleDefaultsCache
        = new(StringComparer.OrdinalIgnoreCase);

    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private string? _defaultLocale;
    
    private bool _isInitialized;
    
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_isInitialized)
        {
            return;
        }
        
        await _initLock.WaitAsync(ct);
        try
        {
            if (_isInitialized)
            {
                return;
            }
            
            await using var _ = new InitializationScope(logger, "bot core");
            
            await registry.InitializeAsync(ct);
            _defaultLocale = registry.GetDefaultLocale();
            await LoadAllTranslationsAsync(ct);

            _isInitialized = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "failed to initialize localization service");
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<string> GetStringAsync(
        string key, 
        string locale, 
        params object[] args)
    {
        if (string.IsNullOrWhiteSpace(key))
            return key;

        var resolvedLocale = registry.ResolveLocale(locale) ?? _defaultLocale ?? "EN_US";
        
        // s0: try cache
        if (TryGetFromCache(resolvedLocale, key, args, out var cached))
            return cached;

        // s1: load from file
        var result = await LoadAndFormatStringAsync(resolvedLocale, key, args);
        
        // s2: fallback chain
        if (result == key && resolvedLocale != _defaultLocale)
        {
            logger.LogDebug("fallback: key '{Key}' not found in {Locale}, trying {Default}", 
                key, resolvedLocale, _defaultLocale);
            result = await LoadAndFormatStringAsync(_defaultLocale!, key, args);
        }

        // s3: final fallback
        if (result == key && args.Length > 0)
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

        // s4: cache the result
        CacheString(resolvedLocale, key, result);

        return result;
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            _translationCache.Clear();
            await registry.ReloadAsync(cancellationToken);
            _defaultLocale = registry.GetDefaultLocale();
            await LoadAllTranslationsAsync(cancellationToken);
            logger.LogInformation("localization cache reloaded");
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public bool IsLocaleRegistered(string locale) => registry.IsLocaleRegistered(locale);
    public string? ResolveLocale(string locale, bool fixNull = true) => fixNull ? registry.ResolveLocale(locale) ?? _defaultLocale ?? "EN_US" : registry.ResolveLocale(locale);

    public void RegisterModuleTranslations(
        string moduleId,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> translations)
    {
        if (translations.Count == 0)
        {
            logger.LogDebug("no translations to register for module. id={ModuleId}", moduleId);
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

        logger.LogDebug(
            "registered translations for module. c={Count}, id={ModuleId}",
            translations.Values.Sum(v => v.Count),
            moduleId);
    }

    private bool TryGetFromCache(string locale, string key, object[] args, out string result)
    {
        result = key;

        // s0: file-based cache
        if (_translationCache.TryGetValue(locale, out var fileLocaleCache))
        {
            if (fileLocaleCache.TryGetValue(key, out var fileTemplate))
            {
                try
                {
                    result = args.Length > 0
                        ? string.Format(CultureInfo.InvariantCulture, fileTemplate, args)
                        : fileTemplate;
                    return true;
                }
                catch (FormatException ex)
                {
                    logger.LogWarning(ex, "failed to format key. key={Key}, locale={Locale}", key, locale);
                }
            }
        }

        // s1: module defaults cache
        if (!_moduleDefaultsCache.TryGetValue(locale, out var moduleLocaleCache))
            return false;

        if (!moduleLocaleCache.TryGetValue(key, out var moduleTemplate))
            return false;

        try
        {
            result = args.Length > 0
                ? string.Format(CultureInfo.InvariantCulture, moduleTemplate, args)
                : moduleTemplate;
            return true;
        }
        catch (FormatException ex)
        {
            logger.LogWarning(ex, "failed to format key. key={Key}, locale={Locale}", key, locale);
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
        var metadata = registry.GetLocaleMetadata(locale);
        if (metadata?.FilePath == null)
            return key;

        var translation = await fileLoader.LoadLocalizationAsync(metadata.FilePath);
        if (translation?.Strings.TryGetValue(key, out var template) != true || template == null)
            return key;

        CacheString(locale, key, template);

        try
        {
            return args.Length > 0
                ? string.Format(CultureInfo.InvariantCulture, template, args)
                : template;
        }
        catch (FormatException ex)
        {
            logger.LogError(ex, "failed to format translation. key={Key}, locale={Locale}", key, locale);
            return template;
        }
    }

    private async Task LoadAllTranslationsAsync(CancellationToken ct)
    {
        foreach (var localeCode in registry.GetAllLocales())
        {
            var metadata = registry.GetLocaleMetadata(localeCode);
            if (metadata?.FilePath == null)
                continue;

            var translation = await fileLoader.LoadLocalizationAsync(metadata.FilePath, ct);
            if (translation?.Strings == null)
                continue;
            
            var localeCache = _translationCache.GetOrAdd(localeCode, _ => 
                new ConcurrentDictionary<string, string>());
            foreach (var (key, value) in translation.Strings)
            {
                localeCache[key] = value;
            }
        }
    }
}