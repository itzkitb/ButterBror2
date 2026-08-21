namespace ButterBror.Domain;

/// <summary>
/// Platform user interface
/// </summary>
public interface IPlatformUser
{
    string Id { get; }
    string DisplayName { get; }
    string Platform { get; }
    HashSet<PlatformPermission> Permissions { get; }
}
