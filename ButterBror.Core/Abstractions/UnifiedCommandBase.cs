using ButterBror.Core.Interfaces;
using ButterBror.Core.Models.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ButterBror.Core.Abstractions;

/// <summary>
/// Base class for unified commands with common functionality
/// </summary>
public abstract class UnifiedCommandBase : IUnifiedCommand
{
    public abstract Task<CommandResult> ExecuteAsync(
        IPlatformChannel channel, 
        List<string> arguments, 
        IPlatformUser user, 
        IServiceProvider services);

    /// <summary>
    /// Helper method to get service from provider
    /// </summary>
    protected T GetService<T>(IServiceProvider services) where T : notnull
    {
        return services.GetRequiredService<T>();
    }

    /// <summary>
    /// Helper method to get logger for command
    /// </summary>
    protected ILogger<T> GetLogger<T>(IServiceProvider services) where T : notnull
    {
        return GetService<ILogger<T>>(services);
    }
}