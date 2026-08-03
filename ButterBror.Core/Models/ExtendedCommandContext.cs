
using ButterBror.Core.Modules.Interfaces;
using ButterBror.Domain;
using ButterBror.Domain.Entities;

namespace ButterBror.Core.Models;

public class ExtendedCommandContext : ICommandContext
{
    public ExtendedCommandContext(ICommandContext originalContext, UserProfile user)
    {
        CommandName = originalContext.CommandName;
        Arguments = originalContext.Arguments;
        User = originalContext.User;
        Channel = originalContext.Channel;
        ExecutedAt = originalContext.ExecutedAt;
        Platform = originalContext.Platform;
        CorrelationId = originalContext.CorrelationId;
        UnifiedUserId = user.UnifiedId;
        Locale = user.PreferredLocale;
        UserProfile = user;
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        CancellationToken = cts.Token;
    }

    public string CommandName { get; }
    public string[] Arguments { get; }
    public IPlatformUser User { get; }
    public UserProfile UserProfile { get; }
    public IPlatformChannel Channel { get; }
    public DateTime ExecutedAt { get; }
    public string Platform { get; }
    public Guid CorrelationId { get; }
    public Guid UnifiedUserId { get; }
    public string Locale { get; }
    public CancellationToken CancellationToken { get; set; }
}
