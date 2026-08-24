using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;

namespace ButterBror.Modules.Loader;

/// <summary>
/// Loading context for dependencies-enabled modules
/// </summary>
public class ModuleAssemblyLoadContext(string name, string modulePath, bool isCollectible, ILogger logger)
    : AssemblyLoadContext(name, isCollectible)
{
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name?.StartsWith("ButterBror.") == true ||
            assemblyName.Name?.StartsWith("Polly.") == true ||
            assemblyName.Name?.StartsWith("Microsoft.Extensions.") == true)
        {
            logger.LogDebug("[skip] {AssemblyName}", assemblyName.Name);
            return null;
        }
        
        var assemblyPath = Path.Combine(modulePath, assemblyName.Name + ".dll");
        if (File.Exists(assemblyPath))
        {
            logger.LogDebug("[load] {AssemblyPath}", assemblyPath);
            return LoadFromAssemblyPath(assemblyPath);
        }

        return null;
    }
}
