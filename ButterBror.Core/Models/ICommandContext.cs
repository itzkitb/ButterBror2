using ButterBror.Core.Interfaces;

namespace ButterBror.Core.Models;

public interface ICommandContext
{
    string CommandName { get; }
    string[] Arguments { get; }
    IPlatformUser User { get; }
    IPlatformChannel Channel { get; }
    DateTime ExecutedAt { get; }
    string Platform { get; }
    Guid CorrelationId { get; }
    CancellationToken CancellationToken { get; set; }
}
