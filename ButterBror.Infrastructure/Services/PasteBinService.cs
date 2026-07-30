using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Web;
using ButterBror.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Registry;

namespace ButterBror.Infrastructure.Services;

public class PasteBinService(
    HttpClient httpClient,
    ILogger<PasteBinService> logger,
    ResiliencePipelineProvider<string> pipelineProvider)
    : IPasteBinService
{
    private readonly ResiliencePipeline _apiPipeline = pipelineProvider.GetPipeline("api");
    
    private const string BaseUrl = "tupid.lol";
    private const string ApiUrl = "https://api.tupid.lol/pb/";
    private const string UploadUrl = $"{ApiUrl}create";
    
    private static readonly Regex KeyPattern = new(@"^[a-zA-Z0-9]{5}$", RegexOptions.Compiled);

    public async Task<string> UploadTextAsync(string content, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Content cannot be empty", nameof(content));
        }

        logger.LogDebug("Uploading text to pastebin (length: {Length})", content.Length);

        return await _apiPipeline.ExecuteAsync(async (ct) =>
        {
            var payload = new CreatePayload { Text = content };
            
            using var response = await httpClient.PostAsJsonAsync(UploadUrl, payload, ct);
            await using var responseStream = await response.Content.ReadAsStreamAsync(ct);
            
            if (!response.IsSuccessStatusCode)
            {
                var errData = await JsonSerializer.DeserializeAsync<ErrorResponse>(responseStream, cancellationToken: ct);
                logger.LogError("Failed to upload to pastebin. HTTP {Status}: {message}", response.StatusCode, errData?.Message);
                return "[API_ERROR]";
            }

            var data = await JsonSerializer.DeserializeAsync<CreateResponse>(responseStream, cancellationToken: ct);
            if (data is not { Status: "ok" })
            {
                logger.LogError("Failed to upload to pastebin: Invalid response or status is not 'ok'");
                return "[API_ERROR]";
            }
            
            logger.LogInformation("Text uploaded to pastebin: {Url}", data.Url);
            return data.Url;
        }, cancellationToken);
    }

    public async Task<string> GetTextAsync(string urlOrKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(urlOrKey))
        {
            throw new ArgumentException("URL or key cannot be empty", nameof(urlOrKey));
        }

        var key = ExtractKey(urlOrKey);
        logger.LogDebug("Retrieving text from pastebin with key: {Key}", key);

        return await _apiPipeline.ExecuteAsync(async (ct) =>
        {
            using var response = await httpClient.GetAsync($"{ApiUrl}{key}", ct);
            await using var responseStream = await response.Content.ReadAsStreamAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var errData = await JsonSerializer.DeserializeAsync<ErrorResponse>(responseStream, cancellationToken: ct);
                logger.LogError("Failed to retrieve text from pastebin. HTTP {Status}: {message}", response.StatusCode, errData?.Message);
                return "[API_ERROR]";
            }
            
            var data = await JsonSerializer.DeserializeAsync<PasteResponse>(responseStream, cancellationToken: ct);
            if (data is not { Status: "ok" })
            {
                logger.LogError("Failed to retrieve text from pastebin: Invalid response or status is not 'ok'");
                return "[API_ERROR]";
            }
            
            logger.LogDebug("Successfully retrieved text from pastebin (length: {Length})", data.Content?.Length ?? 0);
            return data.Content ?? string.Empty;
        }, cancellationToken);
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
            if (Uri.TryCreate(urlOrKey, UriKind.Absolute, out var uri) && 
                uri.Host.Equals(BaseUrl, StringComparison.OrdinalIgnoreCase))
            {
                var queryParameters = HttpUtility.ParseQueryString(uri.Query);
                var key = queryParameters["i"];
                
                if (!string.IsNullOrEmpty(key) && KeyPattern.IsMatch(key))
                {
                    return key;
                }
            }
        }
        catch (Exception)
        {
            // ignored
        }

        throw new ArgumentException(
            $"Invalid pastebin URL or key format: {urlOrKey}. Expected format: https://{BaseUrl}/p?i={{key}} or just {{key}}",
            nameof(urlOrKey));
    }
    
    private record CreatePayload
    {
        [JsonPropertyName("text")] public required string Text { get; init; }
    }
    
    private record Response
    {
        [JsonPropertyName("status")] public required string Status { get; init; }
    }
    
    private record CreateResponse : Response
    {
        [JsonPropertyName("id")] public required string Id { get; init; }
        [JsonPropertyName("url")] public required string Url { get; init; }
    }

    private record PasteResponse : Response
    {
        [JsonPropertyName("id")] public required string Id { get; init; }
        [JsonPropertyName("content")] public required string Content { get; init; }
    }

    private record ErrorResponse : Response
    {
        [JsonPropertyName("message")] public required string Message { get; init; }
    }
}
