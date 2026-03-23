
namespace ButterBror.CommandModule.Context;

/// <summary>
/// Service provider for command
/// </summary>
public interface ICommandServiceProvider
{
    T GetService<T>() where T : notnull;
    T? GetService<T>(string? key = null) where T : notnull;
}
