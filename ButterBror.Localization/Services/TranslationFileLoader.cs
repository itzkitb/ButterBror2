using System.Text.Json;
using ButterBror.Infrastructure.Storage;
using ButterBror.Localization.Models;
using Microsoft.Extensions.Logging;

namespace ButterBror.Localization.Services;

/// <summary>
/// Handles loading and parsing of translation files
/// </summary>
public class TranslationFileLoader
{
    private readonly AppDataStorageProvider _storageProvider;
    private readonly ILogger<TranslationFileLoader> _logger;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    public TranslationFileLoader(
        AppDataStorageProvider storageProvider,
        ILogger<TranslationFileLoader> logger)
    {
        _storageProvider = storageProvider;
        _logger = logger;
    }

    public string GetTranslationFilePath(string fileName)
    {
        var basePath = Path.Combine(_storageProvider.GetAppDataPath(), "Localization");
        Directory.CreateDirectory(basePath);
        return Path.Combine(basePath, fileName);
    }

    public string GetAvailableLocalesPath()
    {
        return GetTranslationFilePath("Available.json");
    }

    public async Task<AvailableLocales?> LoadAvailableLocalesAsync(CancellationToken ct = default)
    {
        var path = GetAvailableLocalesPath();
        return await LoadJsonAsync<AvailableLocales>(path, ct);
    }

    public async Task<TranslationFile?> LoadTranslationAsync(string fileName, CancellationToken ct = default)
    {
        var path = GetTranslationFilePath(fileName);
        return await LoadJsonAsync<TranslationFile>(path, ct);
    }

    public async Task SaveAvailableLocalesAsync(AvailableLocales locales, CancellationToken ct = default)
    {
        var path = GetAvailableLocalesPath();
        await SaveJsonAsync(path, locales, ct);
    }

    public async Task SaveTranslationAsync(string fileName, TranslationFile translation, CancellationToken ct = default)
    {
        var path = GetTranslationFilePath(fileName);
        await SaveJsonAsync(path, translation, ct);
    }

    public bool DeleteTranslationFile(string fileName)
    {
        try
        {
            var path = GetTranslationFilePath(fileName);
            if (File.Exists(path))
            {
                File.Delete(path);
                _logger.LogInformation("Deleted translation file: {FileName}", fileName);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete translation file: {FileName}", fileName);
            return false;
        }
    }

    private async Task<T?> LoadJsonAsync<T>(string path, CancellationToken ct) where T : class
    {
        try
        {
            if (!File.Exists(path))
            {
                _logger.LogWarning("File not found: {Path}", path);
                return null;
            }

            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 
                bufferSize: 4096, useAsync: true);
            
            return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load JSON from {Path}", path);
            return null;
        }
    }

    private async Task SaveJsonAsync<T>(string path, T data, CancellationToken ct) where T : class
    {
        try
        {
            await using var stream = new FileStream(
                path, FileMode.Create, FileAccess.Write, FileShare.None, 
                bufferSize: 4096, useAsync: true);
            
            await JsonSerializer.SerializeAsync(stream, data, _jsonOptions, ct);
            await stream.FlushAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save JSON to {Path}", path);
            throw;
        }
    }
}