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
            throw new ArgumentException("content cannot be empty", nameof(content));
        }

        logger.LogDebug("uploading text to pastebin (length: {Length})", content.Length);

        return await _apiPipeline.ExecuteAsync(async (ct) =>
        {
            var payload = new CreatePayload { Text = content };
            
            using var response = await httpClient.PostAsJsonAsync(UploadUrl, payload, ct);
            await using var responseStream = await response.Content.ReadAsStreamAsync(ct);
            
            if (!response.IsSuccessStatusCode)
            {
                await JsonSerializer.DeserializeAsync<ErrorResponse>(responseStream, cancellationToken: ct);
                logger.LogError("failed to upload to pastebin. http={Status}", response.StatusCode);
                return "[api_error]";
            }

            var data = await JsonSerializer.DeserializeAsync<CreateResponse>(responseStream, cancellationToken: ct);
            if (data is not { Status: "ok" })
            {
                logger.LogError("failed to upload to pastebin: invalid response or status is not 'ok'");
                return "[api_error]";
            }
            
            logger.LogInformation("text uploaded to pastebin. url={Url}", data.Url);
            return data.Url;
        }, cancellationToken);
    }

    public async Task<string> GetTextAsync(string urlOrKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(urlOrKey))
        {
            throw new ArgumentException("url or key cannot be empty", nameof(urlOrKey));
        }

        var key = ExtractKey(urlOrKey);
        logger.LogDebug("retrieving text from pastebin. key={Key}", key);

        return await _apiPipeline.ExecuteAsync(async (ct) =>
        {
            using var response = await httpClient.GetAsync($"{ApiUrl}{key}", ct);
            await using var responseStream = await response.Content.ReadAsStreamAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                await JsonSerializer.DeserializeAsync<ErrorResponse>(responseStream, cancellationToken: ct);
                logger.LogError(
                    "failed to retrieve text from pastebin. http={Status}, key={Key}",
                    response.StatusCode,
                    key);
                return "[api_error]";
            }
            
            var data = await JsonSerializer.DeserializeAsync<PasteResponse>(responseStream, cancellationToken: ct);
            if (data is not { Status: "ok" })
            {
                logger.LogError(
                    "failed to retrieve text from pastebin: invalid response or status is not 'ok'. key={Key}", key);
                return "[api_error]";
            }
            
            logger.LogDebug(
                "successfully retrieved text from pastebin. length={Length}, key={Key}",
                data.Content.Length,
                key);
            return data.Content;
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
            $"invalid pastebin url or key format: {urlOrKey}. expected format: https://{BaseUrl}/p?i={{key}} or just {{key}}",
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
