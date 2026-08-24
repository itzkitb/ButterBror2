using ButterBror.Core.Messaging.Records;

namespace ButterBror.Core.Messaging;

public interface IBbCodeParser
{
    /// <summary>
    /// Parses a BBCode formatted string into a list of MessageParts
    /// </summary>
    List<MessagePart> Parse(string bbCodeText);
}