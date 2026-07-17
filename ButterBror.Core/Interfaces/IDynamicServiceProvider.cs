namespace ButterBror.Core.Interfaces;

/// <summary>
/// Defines a service provider that allows dynamic registration of services at runtime,
/// falling back to a core provider for unresolved types
/// </summary>
public interface IDynamicServiceProvider : IServiceProvider
{
    /// <summary>
    /// Registers a service as a singleton, mapping an interface to an implementation
    /// </summary>
    /// <typeparam name="TService">The type of the service interface to register</typeparam>
    /// <typeparam name="TImplementation">The type of the implementation class</typeparam>
    void AddSingleton<TService, TImplementation>() where TImplementation : class, TService;

    /// <summary>
    /// Registers an existing instance as a singleton
    /// </summary>
    /// <param name="instance">The specific instance of the service to register</param>
    /// <typeparam name="TService">The type of the service interface or class</typeparam>
    void AddSingleton<TService>(TService instance) where TService : class;

    /// <summary>
    /// Registers a service as scoped, mapping an interface to an implementation
    /// </summary>
    /// <typeparam name="TService">The type of the service interface to register</typeparam>
    /// <typeparam name="TImplementation">The type of the implementation class</typeparam>
    void AddScoped<TService, TImplementation>() where TImplementation : class, TService;

    /// <summary>
    /// Registers a service as transient using a factory method
    /// </summary>
    /// <param name="factory">The factory method responsible for creating the service instance</param>
    /// <typeparam name="TService">The type of the service interface or class</typeparam>
    void AddTransient<TService>(Func<IServiceProvider, TService> factory) where TService : class;

    /// <summary>
    /// Registers a service as transient, mapping an interface to an implementation
    /// </summary>
    /// <typeparam name="TService">The type of the service interface to register</typeparam>
    /// <typeparam name="TImplementation">The type of the implementation class</typeparam>
    void AddTransient<TService, TImplementation>() where TImplementation : class, TService;
}