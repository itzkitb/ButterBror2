
using ButterBror.Core.Modules.Interfaces;
using ButterBror.Domain;
using ButterBror.Domain.Entities;

namespace ButterBror.Core.Models;

/*public class ExtendedCommandContext : ICommandContext
{
    public ExtendedCommandContext(ICommandContext originalContext, UserProfile user)
    {
        CommandName = originalContext.CommandName;
        Arguments = originalContext.Arguments;
        PlatformUser = originalContext.PlatformUser;
        Chat = originalContext.Chat;
        ExecutedAt = originalContext.ExecutedAt;
        PlatformId = originalContext.PlatformId;
        CorrelationId = originalContext.CorrelationId;
        UnifiedUserId = user.UnifiedId;
        Locale = user.PreferredLocale;
        UserProfile = user;
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        CancellationToken = cts.Token;
    }

    public string CommandName { get; }
    public string[] Arguments { get; }
    public IPlatformUser PlatformUser { get; }
    public UserProfile UserProfile { get; }
    public IPlatformChannel Chat { get; }
    public DateTime ExecutedAt { get; }
    public string PlatformId { get; }
    public Guid CorrelationId { get; }
    public Guid UnifiedUserId { get; }
    public string Locale { get; }
    public CancellationToken CancellationToken { get; set; }
}
*/