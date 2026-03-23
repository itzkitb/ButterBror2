using ButterBror.CommandModule.Context;
using ButterBror.Domain;

namespace ButterBror.Core.Models;

public class ExtendedCommandContext : ICommandContext
{
    public ExtendedCommandContext(ICommandContext originalContext, Guid unifiedUserId)
    {
        CommandName = originalContext.CommandName;
        Arguments = originalContext.Arguments;
        User = originalContext.User;
        Channel = originalContext.Channel;
        ExecutedAt = originalContext.ExecutedAt;
        Platform = originalContext.Platform;
        CorrelationId = originalContext.CorrelationId;
        UnifiedUserId = unifiedUserId;
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        CancellationToken = cts.Token;
    }

    public string CommandName { get; }
    public string[] Arguments { get; }
    public IPlatformUser User { get; }
    public IPlatformChannel Channel { get; }
    public DateTime ExecutedAt { get; }
    public string Platform { get; }
    public Guid CorrelationId { get; }
    public Guid UnifiedUserId { get; }
    public CancellationToken CancellationToken { get; set; }
}
