using ButterBror.Core.Messaging.Records;

namespace ButterBror.Core.Messaging.Markups;

/// <summary>
/// Fluent builder for constructing an InlineKeyboardMarkup
/// </summary>
public class InlineKeyboardBuilder
{
    private readonly List<List<KeyboardButton>> _rows = [];
    private List<KeyboardButton> _currentRow = [];

    /// <summary>
    /// Add a button to a message (NOT SUPPORTED ON ALL PLATFORMS)
    /// </summary>
    /// <param name="text">Button text</param>
    /// <param name="callbackData">Data that will be returned when clicked</param>
    /// <returns>An instance of this class</returns>
    public InlineKeyboardBuilder AddButton(string text, string callbackData)
    {
        _currentRow.Add(new KeyboardButton(text, callbackData: callbackData));
        return this;
    }

    /// <summary>
    /// Add a url-button to a message (NOT SUPPORTED ON ALL PLATFORMS)
    /// </summary>
    /// <param name="text">Button text</param>
    /// <param name="url">The link that will open when clicked</param>
    /// <returns>An instance of this class</returns>
    public InlineKeyboardBuilder AddUrlButton(string text, string url)
    {
        _currentRow.Add(new KeyboardButton(text, url: url));
        return this;
    }

    /// <summary>
    /// Create a new row for buttons
    /// </summary>
    /// <returns>An instance of this class</returns>
    public InlineKeyboardBuilder NewRow()
    {
        if (_currentRow.Count <= 0)
            return this;
        
        _rows.Add(_currentRow);
        _currentRow = [];
        return this;
    }

    /// <summary>
    /// Finalizes the building process and returns the immutable markup
    /// </summary>
    /// <returns>The finished result</returns>
    public IInteractiveMarkup Build()
    {
        if (_currentRow.Count > 0)
        {
            _rows.Add(_currentRow);
        }
        
        var readOnlyRows = _rows.ConvertAll(r => (IReadOnlyList<KeyboardButton>)r.AsReadOnly());
        return new InlineKeyboardMarkup(readOnlyRows.AsReadOnly());
    }
}