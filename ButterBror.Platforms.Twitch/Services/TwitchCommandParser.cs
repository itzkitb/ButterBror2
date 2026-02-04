using ButterBror.Application.Commands;
using ButterBror.Core.Models;
using ButterBror.Core.Models.Commands;
using ButterBror.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ButterBror.Platforms.Twitch.Services;

public class TwitchCommandParser : ICommandParser
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, Type> _commandTypes = new();

    public TwitchCommandParser(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        RegisterDefaultCommands();
    }

    private void RegisterDefaultCommands()
    {
        _commandTypes.Add("userinfo", typeof(UserInfoCommand));
    }

    public ICommand? ParseCommand(ICommandContext context)
    {
        var commandName = context.CommandName.ToLowerInvariant();

        if (!_commandTypes.TryGetValue(commandName, out var commandType))
        {
            return null;
        }

        // For UserInfoCommand, we create an instance with the correct parameters
        if (commandType == typeof(UserInfoCommand))
        {
            var targetUsername = context.Arguments.Length > 0
                ? context.Arguments[0]
                : context.User.DisplayName;

            return new UserInfoCommand(targetUsername);
        }

        return (ICommand)_serviceProvider.GetRequiredService(commandType);
    }
}