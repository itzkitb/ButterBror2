using ButterBror.Core.Modules.Interfaces;
using ButterBror.Domain;

namespace ButterBror.ChatModules.Twitch.Models;

public class TwitchCommandContext(
    string commandName,
    string[] arguments,
    IPlatformUser user,
    IPlatformChannel channel,
    DateTime executedAt)
    : ICommandContext
{
    public string CommandName { get; } = commandName;
    public string[] Arguments { get; } = arguments;
    public IPlatformUser User { get; } = user;
    public IPlatformChannel Channel { get; } = channel;
    public DateTime ExecutedAt { get; } = executedAt;
    public string Platform { get; } = "sillyapps:twitch";
    public Guid CorrelationId { get; } = Guid.NewGuid();
    public CancellationToken CancellationToken { get; set; }
}
