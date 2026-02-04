using ButterBror.Core.Models;
using ButterBror.Core.Models.Commands;

namespace ButterBror.Infrastructure.Services;

public interface ICommandParser
{
    ICommand? ParseCommand(ICommandContext context);
}