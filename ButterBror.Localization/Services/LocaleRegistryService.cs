using ButterBror.Localization.Models;
using Microsoft.Extensions.Logging;

namespace ButterBror.Localization.Services;

/// <summary>
/// Manages locale registry and alias resolution
/// </summary>
public class LocaleRegistryService
{
    private readonly TranslationFileLoader _fileLoader;
    private readonly ILogger<LocaleRegistryService> _logger;
    
    private AvailableLocales? _registry;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public LocaleRegistryService(
        TranslationFileLoader fileLoader,
        ILogger<LocaleRegistryService> logger)
    {
        _fileLoader = fileLoader;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _registry = await _fileLoader.LoadAvailableLocalesAsync(ct);
            
            if (_registry == null)
            {
                // Create default registry if not exists
                _registry = CreateDefaultRegistry();
                await _fileLoader.SaveAvailableLocalesAsync(_registry, ct);
                _logger.LogInformation("Created default Available.json");
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

        // Direct match
        if (_registry?.Locales.ContainsKey(normalized) == true)
            return normalized;

        // Alias match
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
                _logger.LogWarning("Locale {Locale} already registered", normalized);
                return false;
            }

            _registry.Locales[normalized] = new LocaleMetadata
            {
                FilePath = fileName,
                Aliases = aliases.Select(a => a.Trim()).ToList()
            };

            await _fileLoader.SaveAvailableLocalesAsync(_registry, ct);
            _logger.LogInformation("Registered locale: {Locale}", normalized);
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

            await _fileLoader.SaveAvailableLocalesAsync(_registry!, ct);
            _fileLoader.DeleteTranslationFile(_registry.Locales[resolved].FilePath);
            
            _logger.LogInformation("Unregistered locale: {Locale}", resolved);
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
            _registry = await _fileLoader.LoadAvailableLocalesAsync(ct);
            _logger.LogInformation("Locale registry reloaded");
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