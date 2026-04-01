
using System.Text.Json.Serialization;
using ButterBror.Domain;

namespace ButterBror.CommandModule.Context;

/// <summary>
/// Command execution context with required services
/// </summary>
public interface ICommandExecutionContext
{
    IPlatformChannel Channel { get; }
    List<string> Arguments { get; }
    IPlatformUser User { get; }
    string Locale { get; }
    
    [JsonIgnore]
    CancellationToken CancellationToken { get; }
}
