using ButterBror.Core.Messaging.Enums;

namespace ButterBror.Core.Messaging.Records;

public record MessagePart
{
    /// <summary>
    /// The raw text content of this part
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Combined formatting styles applied to this text
    /// </summary>
    public MessageStyles Styles { get; init; } = MessageStyles.None;

    /// <summary>
    /// URL for link formatting
    /// </summary>
    public string? Url { get; init; }

    /// <summary>
    /// Type identifier for platform-specific extra formatting
    /// </summary>
    public string? ExtraType { get; init; }

    /// <summary>
    /// Additional data for platform-specific extra formatting
    /// </summary>
    public object? ExtraData { get; init; }

    /// <summary>
    /// Indicates if this part is raw text without any formatting
    /// </summary>
    public bool IsRaw { get; init; }
}