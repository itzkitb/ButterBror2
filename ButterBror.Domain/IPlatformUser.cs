namespace ButterBror.Domain;

public interface IPlatformUser
{
    string Id { get; }
    string DisplayName { get; }
    string Platform { get; }
    bool IsModerator { get; }
    bool IsBroadcaster { get; }
}
