
using System.Text.Json.Serialization;
using ButterBror.Domain;

namespace ButterBror.Core.Modules.Interfaces;

/// <summary>
/// Command execution context with required services
/// </summary>
public interface ICommandExecutionContext
{
    IPlatformChannel Channel { get; }
    List<string> Arguments { get; }
    IPlatformUser User { get; }
    string Locale { get; }
    string CommandName { get; }
    
    [JsonIgnore]
    CancellationToken CancellationToken { get; }
}
