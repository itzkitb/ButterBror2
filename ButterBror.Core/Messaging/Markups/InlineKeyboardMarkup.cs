using ButterBror.Core.Messaging.Records;

namespace ButterBror.Core.Messaging.Markups;
/// <summary>
/// Immutable representation of an inline keyboard
/// </summary>
public class InlineKeyboardMarkup(IReadOnlyList<IReadOnlyList<KeyboardButton>> rows) : IInteractiveMarkup
{
    /// <summary>
    /// The rows and columns of buttons
    /// </summary>
    public IReadOnlyList<IReadOnlyList<KeyboardButton>> Rows { get; } = rows;
}