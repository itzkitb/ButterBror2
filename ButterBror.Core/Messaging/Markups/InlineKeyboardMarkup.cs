using ButterBror.Core.Messaging.Records;

namespace ButterBror.Core.Messaging.Markups;
/// <summary>
/// Immutable representation of an inline keyboard
/// </summary>
public class InlineKeyboardMarkup : IInteractiveMarkup
{
    /// <summary>
    /// The rows and columns of buttons
    /// </summary>
    public IReadOnlyList<IReadOnlyList<KeyboardButton>> Rows { get; }

    public InlineKeyboardMarkup(IReadOnlyList<IReadOnlyList<KeyboardButton>> rows)
    {
        Rows = rows;
    }
}