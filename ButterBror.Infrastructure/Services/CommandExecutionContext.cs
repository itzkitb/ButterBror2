using System.Text.Json.Serialization;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Models.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace ButterBror.Infrastructure.Services;

public class CommandExecutionContext : ICommandExecutionContext
{
    public IPlatformChannel Channel { get; }
    public List<string> Arguments { get; }
    public IPlatformUser User { get; }

    [JsonIgnore]
    public CancellationToken CancellationToken { get; }

    public CommandExecutionContext(
        IPlatformChannel channel,
        List<string> arguments,
        IPlatformUser user,
        CancellationToken cancellationToken = default)
    {
        Channel = channel;
        Arguments = arguments;
        User = user;
        CancellationToken = cancellationToken;
    }
}

public class CommandServiceProvider : ICommandServiceProvider
{
    private readonly IServiceProvider _serviceProvider;

    public CommandServiceProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public T GetService<T>() where T : notnull
    {
        return _serviceProvider.GetRequiredService<T>();
    }

    public T? GetService<T>(string? key = null) where T : notnull
    {
        if (key == null)
        {
            return _serviceProvider.GetService<T>();
        }

        var serviceType = typeof(T);
        var namedService = _serviceProvider.GetServices<T>()
            .FirstOrDefault(s => s?.GetType().Name.Contains(key, StringComparison.OrdinalIgnoreCase) == true);

        return namedService ?? _serviceProvider.GetService<T>();
    }
}
