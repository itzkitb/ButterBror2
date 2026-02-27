using System.IO.Compression;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
using ButterBror.ChatModules.Loader;
using ButterBror.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ButterBror.Infrastructure.Services;

/// <summary>
/// Loader for dynamic command modules from AppData/Commands
/// </summary>
public class CommandModuleLoader : IDisposable, ICommandModuleLoader
{
    private readonly IAppDataPathProvider _pathProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CommandModuleLoader> _logger;
    private readonly List<AssemblyLoadContext> _loadContexts = new();
    private readonly List<ICommandModule> _loadedModules = new();
    private readonly List<string> _tempDirectories = new();
    private bool _disposed;

    public CommandModuleLoader(
        IAppDataPathProvider pathProvider,
        IServiceProvider serviceProvider,
        ILogger<CommandModuleLoader> logger)
    {
        _pathProvider = pathProvider;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ICommandModule>> LoadModulesAsync(CancellationToken cancellationToken = default)
    {
        _loadedModules.Clear();
        _loadContexts.Clear();

        var commandsPath = Path.Combine(_pathProvider.GetAppDataPath(), "Command");
        
        if (!Directory.Exists(commandsPath))
        {
            _logger.LogInformation("Commands directory does not exist: {Path}. Creating...", commandsPath);
            Directory.CreateDirectory(commandsPath);
            return Array.Empty<ICommandModule>();
        }

        _logger.LogInformation("Loading command modules from: {Path}", commandsPath);

        // Looking for ZIP archives with command modules
        var moduleFiles = Directory.GetFiles(commandsPath, "*.zip", SearchOption.TopDirectoryOnly);
        
        if (moduleFiles.Length == 0)
        {
            _logger.LogInformation("No command modules (ZIP archives) found in {Path}", commandsPath);
            return Array.Empty<ICommandModule>();
        }

        foreach (var zipFile in moduleFiles)
        {
            try
            {
                var modules = await LoadModuleFromZipAsync(zipFile, cancellationToken);
                _loadedModules.AddRange(modules);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load command module from: {ZipFile}", zipFile);
            }
        }

        _logger.LogInformation("Loaded {Count} command module(s)", _loadedModules.Count);
        return _loadedModules.AsReadOnly();
    }

    private async Task<IReadOnlyList<ICommandModule>> LoadModuleFromZipAsync(string zipPath, CancellationToken cancellationToken)
    {
        var modules = new List<ICommandModule>();

        var tempDir = Path.Combine(Path.GetTempPath(), $"ButterBror_Command_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        _tempDirectories.Add(tempDir);

        try
        {
            _logger.LogDebug("Extracting command module archive: {ZipPath} to {TempDir}", zipPath, tempDir);
            ZipFile.ExtractToDirectory(zipPath, tempDir, overwriteFiles: true);

            // Reading manifest
            var manifestPath = Path.Combine(tempDir, "command.manifest.json");
            CommandModuleManifest? manifest = null;
            
            if (File.Exists(manifestPath))
            {
                var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
                manifest = JsonSerializer.Deserialize<CommandModuleManifest>(manifestJson);
                _logger.LogDebug("Loaded manifest: {ManifestName} v{ManifestVersion}", manifest?.Name, manifest?.Version);
            }

            // Finding main DLL
            string? mainDll = null;
            if (manifest != null && !string.IsNullOrWhiteSpace(manifest.MainDll))
            {
                mainDll = Path.Combine(tempDir, manifest.MainDll);
                if (!File.Exists(mainDll))
                {
                    _logger.LogWarning("Main DLL from manifest not found: {Dll}", manifest.MainDll);
                    mainDll = null;
                }
            }

            if (mainDll == null)
            {
                // Fallback: find first DLL that's not a dependency
                var dllFiles = Directory.GetFiles(tempDir, "*.dll")
                    .FirstOrDefault(f => !f.Contains("System.") && !f.Contains("Microsoft."));
                if (dllFiles != null)
                {
                    mainDll = dllFiles;
                }
            }

            if (mainDll == null)
            {
                _logger.LogWarning("No module DLL found in archive: {ZipPath}", zipPath);
                return modules;
            }

            _logger.LogDebug("Found main module DLL: {Dll}", mainDll);

            // Creating isolated load context
            var moduleName = manifest?.Name ?? Path.GetFileNameWithoutExtension(zipPath);
            var loadContext = new ModuleAssemblyLoadContext(moduleName, tempDir, isCollectible: true, _logger);
            _loadContexts.Add(loadContext);

            // Loading assembly
            var assembly = loadContext.LoadFromAssemblyPath(mainDll);
            _logger.LogDebug("Loaded assembly: {AssemblyName}", assembly.FullName);

            // Finding all classes that implement ICommandModule
            var moduleTypes = assembly.GetTypes()
                .Where(t => typeof(ICommandModule).IsAssignableFrom(t)
                    && !t.IsInterface
                    && !t.IsAbstract)
                .ToList();

            foreach (var moduleType in moduleTypes)
            {
                try
                {
                    var module = Activator.CreateInstance(moduleType);
                    if (module is ICommandModule commandModule)
                    {
                        commandModule.InitializeWithServices(_serviceProvider);
                        modules.Add(commandModule);
                        _logger.LogInformation(
                            "Loaded command module: {ModuleName} v{Version} with {CommandCount} commands",
                            moduleType.Name,
                            commandModule.Version,
                            commandModule.ExportedCommands.Count
                        );
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create instance of module type: {TypeName}", moduleType.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load command module from archive: {ZipPath}", zipPath);
            throw;
        }

        return modules;
    }

    public async Task UnloadModulesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Unloading command modules...");

        // Shutdown all modules
        foreach (var module in _loadedModules)
        {
            try
            {
                await module.ShutdownAsync();
                _logger.LogDebug("Shutdown module: {ModuleId}", module.ModuleId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to shutdown module: {ModuleId}", module.ModuleId);
            }
        }

        // Unload all contexts
        foreach (var context in _loadContexts)
        {
            try
            {
                context.Unload();
                _logger.LogDebug("Unloaded context: {ContextName}", context.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to unload context: {ContextName}", context.Name);
            }
        }

        _loadContexts.Clear();
        _loadedModules.Clear();

        // Clearing temporary directories
        foreach (var tempDir in _tempDirectories)
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                    _logger.LogDebug("Deleted temp directory: {TempDir}", tempDir);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete temp directory: {TempDir}", tempDir);
            }
        }

        _tempDirectories.Clear();

        await Task.Run(() =>
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }, cancellationToken);

        _logger.LogInformation("Command modules unloaded");
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        
        if (disposing)
        {
            foreach (var context in _loadContexts)
            {
                try
                {
                    context.Unload();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to unload context: {ContextName}", context.Name);
                }
            }

            _loadContexts.Clear();
            _loadedModules.Clear();

            foreach (var tempDir in _tempDirectories)
            {
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, recursive: true);
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }

            _tempDirectories.Clear();
        }

        _disposed = true;
    }
}

/// <summary>
/// Command module manifest
/// </summary>
public class CommandModuleManifest
{
    [JsonPropertyName("mainDll")]
    public string MainDll { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }
}