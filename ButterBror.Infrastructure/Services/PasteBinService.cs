using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ButterBror.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Registry;

namespace ButterBror.Infrastructure.Services;

/// <summary>
/// Implementation of Sourceb.in service
/// </summary>
public class PasteBinService : IPasteBinService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PasteBinService> _logger;
    private readonly ResiliencePipeline _apiPipeline;
    
    private const string BaseUrl = "https://sourceb.in";
    private const string ApiUrl = "https://sourceb.in/api/bins";
    private const string CdnUrl = "https://cdn.sourceb.in/bins";
    
    private static readonly Regex KeyPattern = new(@"^[a-zA-Z0-9]{10}$", RegexOptions.Compiled);

    public PasteBinService(
        HttpClient httpClient,
        ILogger<PasteBinService> logger,
        ResiliencePipelineProvider<string> pipelineProvider)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiPipeline = pipelineProvider.GetPipeline("api");
    }

    public async Task<string> UploadTextAsync(string content, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Content cannot be empty", nameof(content));
        }

        _logger.LogDebug("Uploading text to Sourceb.in (length: {Length})", content.Length);

        return await _apiPipeline.ExecuteAsync(async (ct) =>
        {
            var payload = new SourceBinUploadPayload
            {
                Files = new List<SourceBinFile> { new() { Content = content } }
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(ApiUrl, httpContent, ct);
            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync(ct);
            var key = ExtractKeyFromResponse(responseString);

            var fullUrl = $"{BaseUrl}/{key}";
            _logger.LogInformation("Text uploaded to Sourceb.in: {Url}", fullUrl);

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
        _logger.LogDebug("Retrieving text from Sourceb.in with key: {Key}", key);

        return await _apiPipeline.ExecuteAsync(async (ct) =>
        {
            var response = await _httpClient.GetAsync($"{CdnUrl}/{key}/0", ct);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(ct);
            _logger.LogDebug("Successfully retrieved text from Sourceb.in (length: {Length})", content.Length);

            return content;
        }, cancellationToken);
    }

    private static string ExtractKeyFromResponse(string jsonResponse)
    {
        using var doc = JsonDocument.Parse(jsonResponse);
        
        if (doc.RootElement.TryGetProperty("key", out var keyElement))
        {
            var key = keyElement.GetString();
            if (!string.IsNullOrWhiteSpace(key))
            {
                return key;
            }
        }

        throw new InvalidOperationException("Invalid response from Sourceb.in: 'key' property not found or empty");
    }

    private static string ExtractKey(string urlOrKey)
    {
        urlOrKey = urlOrKey.Trim();

        if (KeyPattern.IsMatch(urlOrKey))
        {
            return urlOrKey;
        }

        try
        {
            var uri = new Uri(urlOrKey);
            
            if (uri.Host.Equals("cdn.sourceb.in", StringComparison.OrdinalIgnoreCase))
            {
                var segments = uri.Segments;
                if (segments.Length >= 4 && segments[1].Equals("bins/", StringComparison.OrdinalIgnoreCase))
                {
                    return segments[2].TrimEnd('/');
                }
            }
            
            else if (uri.Host.Equals("sourceb.in", StringComparison.OrdinalIgnoreCase) || 
                     uri.Host.Equals("www.sourceb.in", StringComparison.OrdinalIgnoreCase))
            {
                var segments = uri.Segments;
                if (segments.Length >= 2)
                {
                    return segments[1].TrimEnd('/');
                }
            }
        }
        catch (UriFormatException)
        {
        }

        throw new ArgumentException(
            $"Invalid Sourceb.in URL or key format: {urlOrKey}. Expected format: https://sourceb.in/{{key}} or just {{key}}",
            nameof(urlOrKey));
    }
    
    private class SourceBinUploadPayload
    {
        [JsonPropertyName("files")]
        public List<SourceBinFile> Files { get; set; } = new();
    }

    private class SourceBinFile
    {
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }
}