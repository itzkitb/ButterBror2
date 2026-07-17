using System.Collections.Concurrent;
using ButterBror.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ButterBror.Core;

public class DynamicServiceProvider(IServiceProvider fallbackProvider) : IDynamicServiceProvider, IDisposable
{
    private readonly IServiceProvider _fallbackProvider = fallbackProvider ?? throw new ArgumentNullException(nameof(fallbackProvider));
    
    private readonly ConcurrentDictionary<Type, object> _instances = new();
    internal readonly ConcurrentDictionary<Type, (Type Implementation, ServiceLifetime Lifetime)> Implementations = new();
    internal readonly ConcurrentDictionary<Type, (Func<IServiceProvider, object> Factory, ServiceLifetime Lifetime)> Factories = new();
    private readonly ConcurrentDictionary<Type, Lazy<object>> _singletons = new();
    
    private readonly HashSet<IDisposable> _disposables = new();
    private readonly Lock _disposeLock = new();
    private bool _isDisposed;

    public void AddSingleton<TService, TImplementation>() where TImplementation : class, TService
    {
        ThrowIfDisposed();
        Implementations[typeof(TService)] = (typeof(TImplementation), ServiceLifetime.Singleton);
    }

    public void AddSingleton<TService>(TService instance) where TService : class
    {
        ThrowIfDisposed();
        _instances[typeof(TService)] = instance;
        
        TrackDisposable(instance);
    }

    public void AddScoped<TService, TImplementation>() where TImplementation : class, TService
    {
        ThrowIfDisposed();
        Implementations[typeof(TService)] = (typeof(TImplementation), ServiceLifetime.Scoped);
    }

    public void AddTransient<TService, TImplementation>() where TImplementation : class, TService
    {
        ThrowIfDisposed();
        Implementations[typeof(TService)] = (typeof(TImplementation), ServiceLifetime.Transient);
    }

    public void AddTransient<TService>(Func<IServiceProvider, TService> factory) where TService : class
    {
        ThrowIfDisposed();
        Factories[typeof(TService)] = (factory, ServiceLifetime.Transient);
    }

    public object? GetService(Type serviceType)
    {
        lock (_disposeLock)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(DynamicServiceProvider));
        }

        // Strategy A: Explicit singleton instance
        if (_instances.TryGetValue(serviceType, out var instance))
            return instance;

        // Strategy B: Factory
        if (Factories.TryGetValue(serviceType, out var factoryTuple))
        {
            return factoryTuple.Factory(this);
        }

        // Strategy C: Implementation mapping
        if (Implementations.TryGetValue(serviceType, out var implTuple))
        {
            if (implTuple.Lifetime == ServiceLifetime.Singleton)
            {
                var lazy = _singletons.GetOrAdd(serviceType, _ => new Lazy<object>(() =>
                {
                    var implInstance = CreateInstance(implTuple.Implementation, this);
                    TrackDisposable(implInstance);
                    return implInstance;
                }));
                return lazy.Value;
            }

            // Prevent resolving Scoped services
            if (implTuple.Lifetime == ServiceLifetime.Scoped)
            {
                throw new InvalidOperationException($"Cannot resolve scoped service '{serviceType.Name}' from root provider. Create a scope first");
            }

            // Transient
            return CreateInstance(implTuple.Implementation, this);
        }

        // Strategy D: Fallback to core provider
        return _fallbackProvider.GetService(serviceType);
    }

    public IServiceScope CreateScope()
    {
        ThrowIfDisposed();
        return new DynamicServiceScope(this);
    }

    private object CreateInstance(Type type, IServiceProvider resolver)
    {
        return ActivatorUtilities.CreateInstance(resolver, type);
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

    private void ThrowIfDisposed()
    {
        lock (_disposeLock)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(DynamicServiceProvider));
        }
    }

    public void Dispose()
    {
        lock (_disposeLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            // Clear all singletons
            var disposablesArray = _disposables.ToArray();
            for (int i = _disposables.Count - 1; i >= 0; i--)
            {
                try
                {
                    disposablesArray[i].Dispose();
                }
                catch
                {
                    // hmmm .oO(👤🔫)
                }
            }

            _disposables.Clear();
            _instances.Clear();
            Factories.Clear();
            Implementations.Clear();
            _singletons.Clear();
        }
    }
}