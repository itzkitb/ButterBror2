using System.Text.Json;
using ButterBror.Infrastructure.Storage;
using ButterBror.Localization.Models;
using Microsoft.Extensions.Logging;

namespace ButterBror.Localization.Services;

/// <summary>
/// Handles loading and parsing of translation files
/// </summary>
public class TranslationFileLoader(
    AppDataStorageProvider storageProvider,
    ILogger<TranslationFileLoader> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    public string GetLocalizationFilePath(string fileName)
    {
        var basePath = Path.Combine(storageProvider.GetAppDataPath(), "Localization");
        Directory.CreateDirectory(basePath);
        return Path.Combine(basePath, fileName);
    }

    private string GetAvailableLocalesPath()
    {
        return GetLocalizationFilePath("Available.json");
    }

    public async Task<AvailableLocales?> LoadAvailableLocalesAsync(CancellationToken ct = default)
    {
        var path = GetAvailableLocalesPath();
        return await LoadJsonAsync<AvailableLocales>(path, ct);
    }

    public async Task<TranslationFile?> LoadLocalizationAsync(string fileName, CancellationToken ct = default)
    {
        var path = GetLocalizationFilePath(fileName);
        return await LoadJsonAsync<TranslationFile>(path, ct);
    }

    public async Task SaveAvailableLocalesAsync(AvailableLocales locales, CancellationToken ct = default)
    {
        var path = GetAvailableLocalesPath();
        await SaveJsonAsync(path, locales, ct);
    }

    public async Task SaveLocalizationAsync(string fileName, TranslationFile translation, CancellationToken ct = default)
    {
        var path = GetLocalizationFilePath(fileName);
        await SaveJsonAsync(path, translation, ct);
    }

    public bool DeleteLocalizationFile(string fileName)
    {
        try
        {
            var path = GetLocalizationFilePath(fileName);
            if (File.Exists(path))
            {
                File.Delete(path);
                logger.LogInformation("deleted localization file: {FileName}", fileName);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "failed to delete localization. file={FileName}", fileName);
            return false;
        }
    }

    private async Task<T?> LoadJsonAsync<T>(string path, CancellationToken ct) where T : class
    {
        try
        {
            if (!File.Exists(path))
            {
                logger.LogWarning("file not found. path={Path}", path);
                return null;
            }

            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 
                bufferSize: 4096, useAsync: true);
            
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "failed to load json. path={Path}", path);
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
            
            await JsonSerializer.SerializeAsync(stream, data, JsonOptions, ct);
            await stream.FlushAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "failed to save json. path={Path}", path);
            throw;
        }
    }
}