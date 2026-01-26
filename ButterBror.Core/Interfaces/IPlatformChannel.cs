namespace ButterBror.Core.Interfaces;

public interface IPlatformChannel
{
    string Id { get; }
    string Name { get; }
    string Platform { get; }
}