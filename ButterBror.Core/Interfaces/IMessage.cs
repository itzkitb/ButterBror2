namespace ButterBror.Core.Interfaces;

public interface IMessage
{
    string Content { get; }
    IPlatformUser Sender { get; }
    IPlatformChannel Channel { get; }
    DateTime Timestamp { get; }
    string Platform { get; }
}
