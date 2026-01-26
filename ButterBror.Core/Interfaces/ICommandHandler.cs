using ButterBror.Core.Models;
using ButterBror.Core.Models.Commands;

namespace ButterBror.Core.Interfaces;

public interface ICommandHandler<in TCommand> where TCommand : ICommand
{
    Task<CommandResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}
