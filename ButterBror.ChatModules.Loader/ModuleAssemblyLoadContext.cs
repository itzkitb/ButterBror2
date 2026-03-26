using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;

namespace ButterBror.Modules.Loader;

/// <summary>
/// Loading context for dependencies-enabled modules
/// </summary>
public class ModuleAssemblyLoadContext : AssemblyLoadContext
{
    private readonly string _modulePath;
    private readonly ILogger _logger;

    public ModuleAssemblyLoadContext(string name, string modulePath, bool isCollectible, ILogger logger)
        : base(name, isCollectible)
    {
        _modulePath = modulePath;
        _logger = logger;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name?.StartsWith("ButterBror.") == true)
        {
            _logger.LogDebug("Skipping ButterBror assembly: {AssemblyName}", assemblyName.Name);
            return null;
        }

        if (assemblyName.Name?.StartsWith("Polly.") == true ||
            assemblyName.Name?.StartsWith("Microsoft.Extensions.") == true)
        {
            _logger.LogDebug("Skipping Polly/Resilience assembly: {AssemblyName}", assemblyName.Name);
            return null;
        }

        // Trying to load from the module's temporary directory
        var assemblyPath = Path.Combine(_modulePath, assemblyName.Name + ".dll");
        if (File.Exists(assemblyPath))
        {
            _logger.LogDebug("Loading assembly from module path: {AssemblyPath}", assemblyPath);
            return LoadFromAssemblyPath(assemblyPath);
        }

        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        return base.LoadUnmanagedDll(unmanagedDllName);
    }
}
