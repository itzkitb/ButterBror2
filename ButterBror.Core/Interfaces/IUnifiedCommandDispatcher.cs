using ButterBror.Core.Interfaces;
using ButterBror.Core.Models.Commands;

namespace ButterBror.Core.Interfaces;

public interface IUnifiedCommandDispatcher
{
    Task<CommandResult> DispatchAsync(
        string commandName,
        IPlatformChannel channel,
        List<string> arguments,
        IPlatformUser user,
        IServiceProvider services);
}