namespace ButterBror.Core.Interfaces;

/// <summary>
/// Service for interacting with HasteBin.dev API
/// </summary>
public interface IHasteBinService
{
    /// <summary>
    /// Upload text to server
    /// </summary>
    /// <param name="content">Text content to upload</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>URL to the uploaded text</returns>
    Task<string> UploadTextAsync(string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve text from server by URL or key
    /// </summary>
    /// <param name="urlOrKey">URL or the key</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Raw text content</returns>
    Task<string> GetTextAsync(string urlOrKey, CancellationToken cancellationToken = default);
}