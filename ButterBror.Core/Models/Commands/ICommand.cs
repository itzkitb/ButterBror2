using MediatR;

namespace ButterBror.Core.Models.Commands;

public interface ICommand : IRequest<CommandResult>
{
}