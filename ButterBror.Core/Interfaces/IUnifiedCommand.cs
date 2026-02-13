using ButterBror.Core.Interfaces;
using ButterBror.Core.Models.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace ButterBror.Core.Interfaces;

/// <summary>
/// Unified command interface that receives only essential data
/// </summary>
public interface IUnifiedCommand
{
    /// <summary>
    /// Executes the command with minimal required context
    /// </summary>
    /// <param name="channel">Current chat/channel data</param>
    /// <param name="arguments">Command arguments as list of strings</param>
    /// <param name="user">User who invoked the command</param>
    /// <param name="services">Service collection for dependency injection</param>
    /// <returns>Command result</returns>
    Task<CommandResult> ExecuteAsync(
        IPlatformChannel channel, 
        List<string> arguments, 
        IPlatformUser user, 
        IServiceProvider services);
}