using ButterBror.Core.Messaging.Records;

namespace ButterBror.Core.Messaging;

/// <summary>
/// The unified message object containing parsed parts and metadata
/// </summary>
public class Message
{
    public Message(
            string rawText,
            IInteractiveMarkup? markup = null,
            IEnumerable<Attachment>? attachments = null)
        : this(rawText, new BBCodeParser(), markup, attachments)
    {
    }
    
    public Message(
        string rawText,
        IBBCodeParser parser,
        IInteractiveMarkup? markup = null,
        IEnumerable<Attachment>? attachments = null)
    {
        if (parser == null) throw new ArgumentNullException(nameof(parser));
        if (string.IsNullOrEmpty(rawText) && (attachments == null || !attachments.Any()))
        {
            throw new ArgumentException("Message must contain either text or at least one attachment");
        }
        
        // S0: Parse the BBCode text into structured parts
        var parsedParts = parser.Parse(rawText ?? string.Empty);
        Parts = parsedParts.AsReadOnly();
        
        // S1: Reconstruct RawText from parsed parts
        RawText = string.Concat(parsedParts.Select(p => p.Text));
        InteractiveMarkup = markup;
        Attachments = (attachments ?? Enumerable.Empty<Attachment>()).ToList().AsReadOnly();
    }
    
    /// <summary>
    /// The parsed list of message parts
    /// </summary>
    public IReadOnlyList<MessagePart> Parts { get; }

    /// <summary>
    /// Raw message
    /// </summary>
    public string RawText { get; }
    
    /// <summary>
    /// Inline keyboard or other reply markup
    /// </summary>
    public IInteractiveMarkup? InteractiveMarkup { get; }
    
    /// <summary>
    /// List of media attachments
    /// </summary>
    public IReadOnlyList<Attachment> Attachments { get; }
}