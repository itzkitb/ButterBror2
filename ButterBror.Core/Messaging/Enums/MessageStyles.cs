namespace ButterBror.Core.Messaging.Enums;

/// <summary>
/// Text formatting styles
/// </summary>
[Flags]
public enum MessageStyles
{
    None = 0,
    Bold = 1 << 0,
    Italic = 1 << 1,
    Underline = 1 << 2,
    Strikethrough = 1 << 3,
    Quote = 1 << 4,
    Monospace = 1 << 5,
    Spoiler = 1 << 6
}