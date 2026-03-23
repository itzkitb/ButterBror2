namespace ButterBror.Domain;

public interface IPlatformChannel
{
    string Id { get; }
    string Name { get; }
    string Platform { get; }
}
