using ButterBror.Core.Messaging.Enums;

namespace ButterBror.Core.Messaging.Records;

/// <summary>
/// Represents a media attachment for a message.
/// Uses static factory methods to ensure valid combinations of properties
/// </summary>
public record Attachment(
    AttachmentType Type,
    string? FileId = null,
    string? Url = null,
    Stream? FileStream = null,
    string? FileName = null,
    string? Caption = null
)
{
    /// <summary>
    /// Creates an attachment from a file already uploaded to the messenger (e.g., Telegram FileId)
    /// </summary>
    public static Attachment FromFileId(AttachmentType type, string fileId, string? caption = null)
    {
        if (string.IsNullOrWhiteSpace(fileId)) throw new ArgumentException("FileId cannot be empty.", nameof(fileId));
        return new Attachment(type, FileId: fileId, Caption: caption);
    }

    /// <summary>
    /// Creates an attachment from a public URL
    /// </summary>
    public static Attachment FromUrl(AttachmentType type, string url, string? caption = null)
    {
        if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("URL cannot be empty.", nameof(url));
        return new Attachment(type, Url: url, Caption: caption);
    }

    /// <summary>
    /// Creates an attachment from a local stream (e.g., file from disk or memory)
    /// </summary>
    public static Attachment FromStream(AttachmentType type, Stream stream, string fileName, string? caption = null)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("FileName cannot be empty.", nameof(fileName));
        return new Attachment(type, FileStream: stream, FileName: fileName, Caption: caption);
    }
}