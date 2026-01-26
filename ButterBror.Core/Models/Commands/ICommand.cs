using MediatR;

namespace ButterBror.Core.Models.Commands;

public interface ICommand<out TResponse> : IRequest<TResponse>
{
}

public interface ICommand : IRequest<CommandResult>
{
}