using ButterBror.ChatModules.Twitch.Commands;
using ButterBror.ChatModules.Twitch.Interfaces;
using ButterBror.ChatModules.Twitch.Models;
using ButterBror.Core.Modules;
using ButterBror.Core.Modules.Interfaces;
using Microsoft.Extensions.Options;

namespace ButterBror.ChatModules.Twitch;

public class CommandFactories
{
    private readonly Func<ICommand> _joinCommandFactory;
    private readonly Func<ICommand> _partCommandFactory;
    private readonly Func<ICommand> _setPrefixCommandFactory;
    private readonly Func<ICommand> _authCommandFactory;
    private readonly Func<ICommand> _addChannelCommandFactory;
    private readonly Func<ICommand> _deleteChannelCommandFactory;
    private readonly Func<ICommand> _channelSettingsCommandFactory;
    
    internal IReadOnlyList<ModuleCommandExport> Export =>
    [
        new("join", _joinCommandFactory, new JoinChannelCommandMetadata()),
        new("part", _partCommandFactory, new PartChannelCommandMetadata()),
        new("setprefix", _setPrefixCommandFactory, new SetPrefixCommandMetadata()),
        new("auth", _authCommandFactory, new AuthCommandMetadata()),
        new("addchannel", _addChannelCommandFactory, new AddChannelCommandMetadata()),
        new("deletechannel", _deleteChannelCommandFactory, new DeleteChannelCommandMetadata()),
        new("twitchset", _channelSettingsCommandFactory, new ChannelSettingsCommandMetadata())
    ];

    internal CommandFactories(
        TwitchModule twitchModule,
        ITwitchClient twitchClient,
        IOptions<TwitchConfiguration> config,
        ITwitchChannelManager channelManager,
        IServiceProvider serviceProvider,
        ITwitchNotificationService notificationService)
    {
        _joinCommandFactory = () => new JoinChannelCommand(serviceProvider, twitchClient, channelManager, notificationService);
        _partCommandFactory = () => new PartChannelCommand(serviceProvider, twitchClient, channelManager, notificationService);
        _setPrefixCommandFactory = () => new SetPrefixCommand(serviceProvider, twitchModule);
        _authCommandFactory = () => new AuthCommand(serviceProvider, config);
        _addChannelCommandFactory = () => new AddChannelCommand(serviceProvider, twitchClient, channelManager, notificationService);
        _deleteChannelCommandFactory = () => new DeleteChannelCommand(serviceProvider, twitchClient, channelManager, notificationService);
        _channelSettingsCommandFactory = () => new ChannelSettingsCommand(serviceProvider, twitchClient);
    }
}