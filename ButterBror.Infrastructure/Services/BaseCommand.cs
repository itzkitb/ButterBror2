using ButterBror.Core.Models.Commands;

namespace ButterBror.Infrastructure.Services;

public record BaseCommand(string CommandName, string[] Arguments) : ICommand;