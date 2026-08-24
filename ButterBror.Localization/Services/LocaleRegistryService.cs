using ButterBror.Localization.Models;
using Microsoft.Extensions.Logging;

namespace ButterBror.Localization.Services;

/// <summary>
/// Manages locale registry and alias resolution
/// </summary>
public class LocaleRegistryService(
    TranslationFileLoader fileLoader,
    ILogger<LocaleRegistryService> logger)
{
    private AvailableLocales? _registry;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _registry = await fileLoader.LoadAvailableLocalesAsync(ct);
            
            if (_registry == null)
            {
                _registry = CreateDefaultRegistry();
                await fileLoader.SaveAvailableLocalesAsync(_registry, ct);
                logger.LogInformation("created default Available.json");
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public string GetDefaultLocale() => _registry?.DefaultLocale ?? "EN_US";

    public string? ResolveLocale(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var normalized = input.Trim().ToUpperInvariant();

        // direct match
        if (_registry?.Locales.ContainsKey(normalized) == true)
            return normalized;

        // alias match
        foreach (var (localeCode, metadata) in _registry?.Locales ?? new Dictionary<string, LocaleMetadata>())
        {
            if (metadata.Aliases.Any(a => 
                a.Trim().Equals(input, StringComparison.OrdinalIgnoreCase)))
            {
                return localeCode;
            }
        }

        return null;
    }

    public bool IsLocaleRegistered(string input) => ResolveLocale(input) != null;

    public IEnumerable<string> GetAllLocales() => 
        _registry?.Locales.Keys ?? Enumerable.Empty<string>();

    public LocaleMetadata? GetLocaleMetadata(string locale)
    {
        var resolved = ResolveLocale(locale);
        return resolved != null && _registry?.Locales.TryGetValue(resolved, out var meta) == true 
            ? meta 
            : null;
    }

    public async Task<bool> RegisterLocaleAsync(
        string localeCode, 
        string fileName, 
        IEnumerable<string> aliases,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_registry == null)
                await InitializeAsync(ct);

            var normalized = localeCode.Trim().ToUpperInvariant();
            
            if (_registry!.Locales.ContainsKey(normalized))
            {
                logger.LogWarning("locale {Locale} already registered", normalized);
                return false;
            }

            _registry.Locales[normalized] = new LocaleMetadata
            {
                FilePath = fileName,
                Aliases = aliases.Select(a => a.Trim()).ToList()
            };

            await fileLoader.SaveAvailableLocalesAsync(_registry, ct);
            logger.LogInformation("registered locale. locale={Locale}", normalized);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> UnregisterLocaleAsync(string localeCode, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var resolved = ResolveLocale(localeCode);
            if (resolved == null || _registry?.Locales.Remove(resolved) != true)
                return false;

            await fileLoader.SaveAvailableLocalesAsync(_registry!, ct);
            fileLoader.DeleteLocalizationFile(_registry.Locales[resolved].FilePath);
            
            logger.LogInformation("unregistered locale. locale={Locale}", resolved);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _registry = await fileLoader.LoadAvailableLocalesAsync(ct);
            logger.LogInformation("locale registry reloaded");
        }
        finally
        {
            _lock.Release();
        }
    }

    private static AvailableLocales CreateDefaultRegistry()
    {
        return new AvailableLocales
        {
            DefaultLocale = "EN_US",
            Locales = new Dictionary<string, LocaleMetadata>
            {
                ["EN_US"] = new()
                {
                    FilePath = "EN_US.json",
                    Aliases = new() { "en", "english", "en-US" }
                },
                ["RU_RU"] = new()
                {
                    FilePath = "RU_RU.json",
                    Aliases = new() { "ru", "russian", "ru-RU" }
                }
            }
        };
    }
}