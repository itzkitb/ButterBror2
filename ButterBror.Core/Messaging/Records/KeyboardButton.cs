namespace ButterBror.Core.Messaging.Records;

/// <summary>
/// Represents a single button in an interactive keyboard.
/// Implemented as a 'record' to ensure immutability and value-based equality
/// </summary>
public record KeyboardButton
{
    public string Text { get; init; }
    public string? CallbackData { get; init; }
    public string? Url { get; init; }

    public KeyboardButton(string text, string? callbackData = null, string? url = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Button text cannot be empty.", nameof(text));
            
        if (!string.IsNullOrEmpty(callbackData) && !string.IsNullOrEmpty(url))
            throw new InvalidOperationException("A button cannot have both CallbackData and Url.");

        Text = text;
        CallbackData = callbackData;
        Url = url;
    }
}