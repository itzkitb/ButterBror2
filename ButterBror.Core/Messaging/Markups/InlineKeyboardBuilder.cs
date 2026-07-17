using ButterBror.Core.Messaging.Records;

namespace ButterBror.Core.Messaging.Markups;

/// <summary>
/// Fluent builder for constructing an InlineKeyboardMarkup
/// </summary>
public class InlineKeyboardBuilder
{
    private readonly List<List<KeyboardButton>> _rows = new();
    private List<KeyboardButton> _currentRow = new();

    public InlineKeyboardBuilder AddButton(string text, string callbackData)
    {
        _currentRow.Add(new KeyboardButton(text, callbackData: callbackData));
        return this;
    }

    public InlineKeyboardBuilder AddUrlButton(string text, string url)
    {
        _currentRow.Add(new KeyboardButton(text, url: url));
        return this;
    }

    public InlineKeyboardBuilder NewRow()
    {
        if (_currentRow.Count > 0)
        {
            _rows.Add(_currentRow);
            _currentRow = new List<KeyboardButton>();
        }
        return this;
    }

    /// <summary>
    /// Finalizes the building process and returns the immutable markup
    /// </summary>
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