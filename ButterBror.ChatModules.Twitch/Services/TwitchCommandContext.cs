using ButterBror.Domain;
using ButterBror.CommandModule.Context;

namespace ButterBror.ChatModules.Twitch.Services;

public class TwitchCommandContext : ICommandContext
{
    public TwitchCommandContext(string commandName, string[] arguments,
                               IPlatformUser user, IPlatformChannel channel, DateTime executedAt)
    {
        CommandName = commandName;
        Arguments = arguments;
        User = user;
        Channel = channel;
        ExecutedAt = executedAt;
        Platform = "sillyapps:twitch";
        CorrelationId = Guid.NewGuid();
    }

    public string CommandName { get; }
    public string[] Arguments { get; }
    public IPlatformUser User { get; }
    public IPlatformChannel Channel { get; }
    public DateTime ExecutedAt { get; }
    public string Platform { get; }
    public Guid CorrelationId { get; }
    public CancellationToken CancellationToken { get; set; }
}
