using System.Text.Json.Serialization;
using ButterBror.CommandModule.Context;
using ButterBror.Core.Interfaces;
using ButterBror.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace ButterBror.Infrastructure.Services;

public class CommandExecutionContext(
    IPlatformChannel channel,
    List<string> arguments,
    IPlatformUser user,
    string locale,
    string commandName,
    CancellationToken cancellationToken = default)
    : ICommandExecutionContext
{
    public IPlatformChannel Channel { get; } = channel;
    public List<string> Arguments { get; } = arguments;
    public IPlatformUser User { get; } = user;
    public string Locale { get; } = locale;
    public string CommandName { get; } = commandName;

    [JsonIgnore]
    public CancellationToken CancellationToken { get; } = cancellationToken;
}

public class CommandServiceProvider(IServiceProvider serviceProvider) : ICommandServiceProvider
{
    private readonly IDynamicServiceProvider? _dynamicProvider = serviceProvider.GetService<IDynamicServiceProvider>();

    public T GetService<T>() where T : notnull
    {
        var service = _dynamicProvider?.GetService(typeof(T)) ?? serviceProvider.GetService(typeof(T));
        
        return service is T typedService 
            ? typedService 
            : throw new InvalidOperationException($"Service of type {typeof(T).Name} is not registered.");
    }

    public T? GetService<T>(string? key = null) where T : notnull
    {
        if (key == null)
        {
            return serviceProvider.GetService<T>();
        }
        
        var services = _dynamicProvider != null 
            ? _dynamicProvider.GetServices<T>() 
            : serviceProvider.GetServices<T>();

        var namedService = services.FirstOrDefault(s => 
            s?.GetType().Name.Contains(key, StringComparison.OrdinalIgnoreCase) == true);

        return namedService ?? GetService<T>();
    }
}
