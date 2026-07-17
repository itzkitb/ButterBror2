using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace ButterBror.Core;

/// <summary>
/// Represents a scope for dynamically resolved services
/// </summary>
internal class DynamicServiceScope(DynamicServiceProvider rootProvider) : IServiceScope
{
    private readonly ScopedDynamicServiceProvider _scopedProvider = new(rootProvider);
    private bool _isDisposed;

    public IServiceProvider ServiceProvider => _scopedProvider;

    public void Dispose()
    {
        if (_isDisposed) return;
        _scopedProvider.Dispose();
        _isDisposed = true;
    }
}

/// <summary>
/// Internal provider that handles Scoped and Transient resolution within a specific scope
/// </summary>
internal class ScopedDynamicServiceProvider(DynamicServiceProvider root) : IServiceProvider, IDisposable
{
    private readonly ConcurrentDictionary<Type, Lazy<object>> _scopedInstances = new();
    private readonly HashSet<IDisposable> _disposables = new();
    private readonly Lock _disposeLock = new();
    private bool _isDisposed;

    public object? GetService(Type serviceType)
    {
        lock (_disposeLock)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(ScopedDynamicServiceProvider));
        }
        
        if (root.Factories.TryGetValue(serviceType, out var factoryTuple))
        {
            var instance = factoryTuple.Factory(this); 
            TrackDisposable(instance);
            return instance;
        }

        if (root.Implementations.TryGetValue(serviceType, out var implTuple))
        {
            if (implTuple.Lifetime == ServiceLifetime.Scoped)
            {
                var lazy = _scopedInstances.GetOrAdd(serviceType, _ => new Lazy<object>(() => 
                    CreateInstance(implTuple.Implementation)
                ));

                var instance = lazy.Value;
                TrackDisposable(instance); 
                return instance;
            }

            if (implTuple.Lifetime == ServiceLifetime.Transient)
            {
                var instance = CreateInstance(implTuple.Implementation);
                TrackDisposable(instance);
                return instance;
            }
        }

        return root.GetService(serviceType);
    }

    private object CreateInstance(Type type)
    {
        return ActivatorUtilities.CreateInstance(this, type);
    }

    private void TrackDisposable(object? instance)
    {
        if (instance is IDisposable disposable)
        {
            lock (_disposeLock)
            {
                if (_isDisposed)
                {
                    disposable.Dispose();
                    return;
                }

                _disposables.Add(disposable);
            }
        }
    }

    public void Dispose()
    {
        lock (_disposeLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            
            var disposablesArray = _disposables.ToArray();
            for (int i = _disposables.Count - 1; i >= 0; i--)
            {
                try
                {
                    disposablesArray[i].Dispose();
                }
                catch
                {
                    // lmao
                }
            }
            _disposables.Clear();
            _scopedInstances.Clear();
        }
    }
}