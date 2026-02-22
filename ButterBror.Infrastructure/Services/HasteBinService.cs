using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ButterBror.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Polly;

namespace ButterBror.Infrastructure.Services;

/// <summary>
/// Implementation of HasteBin.dev service
/// </summary>
public class HasteBinService : IHasteBinService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HasteBinService> _logger;
    private readonly ResiliencePipeline _apiPipeline;
    private const string BaseUrl = "https://hastebin.dev";
    private static readonly Regex KeyPattern = new(@"^[a-zA-Z0-9]{10}$", RegexOptions.Compiled);

    public HasteBinService(
        HttpClient httpClient,
        ILogger<HasteBinService> logger,
        ResiliencePipeline apiPipeline)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiPipeline = apiPipeline;
    }

    public async Task<string> UploadTextAsync(string content, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Content cannot be empty", nameof(content));
        }

        _logger.LogDebug("Uploading text to HasteBin (length: {Length})", content.Length);

        return await _apiPipeline.ExecuteAsync(async (ct) =>
        {
            var httpContent = new StringContent(content, Encoding.UTF8, "text/plain");
            var response = await _httpClient.PostAsync($"{BaseUrl}/documents", httpContent, ct);
            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync(ct);
            var key = ExtractKeyFromResponse(responseString);

            var fullUrl = $"{BaseUrl}/{key}";
            _logger.LogInformation("Text uploaded to HasteBin: {Url}", fullUrl);

            return fullUrl;
        }, cancellationToken);
    }

    public async Task<string> GetTextAsync(string urlOrKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(urlOrKey))
        {
            throw new ArgumentException("URL or key cannot be empty", nameof(urlOrKey));
        }

        var key = ExtractKey(urlOrKey);
        _logger.LogDebug("Retrieving text from HasteBin with key: {Key}", key);

        return await _apiPipeline.ExecuteAsync(async (ct) =>
        {
            var response = await _httpClient.GetAsync($"{BaseUrl}/raw/{key}", ct);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(ct);
            _logger.LogDebug("Successfully retrieved text from HasteBin (length: {Length})", content.Length);

            return content;
        }, cancellationToken);
    }

    private static string ExtractKeyFromResponse(string jsonResponse)
    {
        using var doc = JsonDocument.Parse(jsonResponse);
        var key = doc.RootElement.GetProperty("key").GetString();

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("Invalid response from HasteBin: key not found");
        }

        return key;
    }

    private static string ExtractKey(string urlOrKey)
    {
        // If it's already a key
        if (KeyPattern.IsMatch(urlOrKey))
        {
            return urlOrKey;
        }

        // Parse URL: https://hastebin.dev/{key} or https://hastebin.dev/raw/{key}
        try
        {
            var uri = new Uri(urlOrKey);
            var segments = uri.Segments;

            // Handle /raw/{key}
            if (segments.Length >= 3 && segments[1].Equals("raw/", StringComparison.OrdinalIgnoreCase))
            {
                return segments[2].TrimEnd('/');
            }

            // Handle /{key}
            if (segments.Length >= 2)
            {
                return segments[1].TrimEnd('/');
            }
        }
        catch (UriFormatException)
        {
            // Not a valid URI, treat as key
        }

        throw new ArgumentException(
            $"Invalid HasteBin URL or key format: {urlOrKey}. Expected format: https://hastebin.dev/{{key}} or just {{key}}",
            nameof(urlOrKey));
    }
}