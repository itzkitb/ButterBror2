using ButterBror.Core.Contracts;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Models.Commands;

namespace ButterBror.ChatModules.Abstractions;

/// <summary>
/// Basic interface for chat modules
/// </summary>
public interface IChatModule : IPlatformModule
{
}

/// <summary>
/// Interface for modules that require service initialization
/// </summary>
public interface IChatModuleWithServices : IChatModule
{
    /// <summary>
    /// Module initialization
    /// </summary>
    void InitializeWithServices(IServiceProvider serviceProvider);
}
