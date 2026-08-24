namespace ButterBror.Domain;

/// <summary>
/// Platform chat interface
/// </summary>
public interface IPlatformChat
{
    string Id { get; }
    string Name { get; }
    string Platform { get; }
}
