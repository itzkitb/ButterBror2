namespace ButterBror.Core.Interfaces;

public interface IPlatformUser
{
    string Id { get; }
    string DisplayName { get; }
    string Platform { get; }
    bool IsModerator { get; }
    bool IsBroadcaster { get; }
}
