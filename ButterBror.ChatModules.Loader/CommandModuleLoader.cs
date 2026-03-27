using System.IO.Compression;
using System.Runtime.Loader;
using System.Text.Json;
using ButterBror.CommandModule.CommandModule;
using ButterBror.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ButterBror.Modules.Loader;

/// <summary>
/// Loader for dynamic command modules
/// </summary>
public class CommandModuleLoader : IDisposable, ICommandModuleLoader
{
    private readonly IAppDataPathProvider _pathProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CommandModuleLoader> _logger;
    private readonly ILocalizationService _localizationService;
    private readonly List<AssemblyLoadContext> _loadContexts = new();
    private readonly List<ICommandModule> _loadedModules = new();
    private readonly List<string> _tempDirectories = new();
    private readonly Dictionary<string, string> _moduleIdToArchivePath = new();
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private bool _disposed;

    private const string ManifestFileName = "module.manifest.json";

    public CommandModuleLoader(
        IAppDataPathProvider pathProvider,
        IServiceProvider serviceProvider,
        ILogger<CommandModuleLoader> logger,
        ILocalizationService localizationService)
    {
        _pathProvider = pathProvider;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _localizationService = localizationService;
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

        // Looking for archives with command modules
        var moduleFiles = Directory.GetFiles(commandsPath, "*.pag", SearchOption.TopDirectoryOnly);
        
        if (moduleFiles.Length == 0)
        {
            _logger.LogInformation("No command modules found in {Path}", commandsPath);
            return Array.Empty<ICommandModule>();
        }

        foreach (var file in moduleFiles)
        {
            try
            {
                var modules = await LoadModuleFromArchiveAsync(file, cancellationToken);
                _loadedModules.AddRange(modules);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load command module from: {File}", file);
            }
        }

        _logger.LogInformation("Loaded {Count} command module(s)", _loadedModules.Count);
        return _loadedModules.AsReadOnly();
    }

    private async Task<IReadOnlyList<ICommandModule>> LoadModuleFromArchiveAsync(string path, CancellationToken cancellationToken)
    {
        var modules = new List<ICommandModule>();

        var tempDir = Path.Combine(Path.GetTempPath(), $"ButterBror_Command_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        _tempDirectories.Add(tempDir);

        try
        {
            _logger.LogDebug("Extracting command module archive: {Path} to {TempDir}", path, tempDir);
            ZipFile.ExtractToDirectory(path, tempDir, overwriteFiles: true);

            // S0: Reading manifest
            var manifestPath = Path.Combine(tempDir, ManifestFileName);
            CommandModuleManifest? manifest = null;
            
            if (File.Exists(manifestPath))
            {
                var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
                manifest = JsonSerializer.Deserialize<CommandModuleManifest>(manifestJson);
                _logger.LogDebug("Loaded manifest: {ManifestName} v.{ManifestVersion}", manifest?.Name, manifest?.Version);
            }

            // S1: Finding main DLL
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
                // S2: Find first DLL that's not a dependency
                var dllFiles = Directory.GetFiles(tempDir, "*.dll")
                    .FirstOrDefault(f => !f.Contains("System.") && !f.Contains("Microsoft."));
                if (dllFiles != null)
                {
                    mainDll = dllFiles;
                }
            }

            if (mainDll == null)
            {
                _logger.LogWarning("No module DLL found in archive: {Path}", path);
                return modules;
            }

            _logger.LogDebug("Found main module DLL: {Dll}", mainDll);

            // S2: Creating isolated context
            var moduleName = manifest?.Name ?? Path.GetFileNameWithoutExtension(path);
            var loadContext = new ModuleAssemblyLoadContext(moduleName, tempDir, isCollectible: true, _logger);
            _loadContexts.Add(loadContext);

            // S3: Loading assembly
            var assembly = loadContext.LoadFromAssemblyPath(mainDll);
            _logger.LogDebug("Loaded assembly: {AssemblyName}", assembly.FullName);

            // S4: Finding all classes that implement ICommandModule
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
                        // Store mapping from module ID to archive path
                        _moduleIdToArchivePath[commandModule.ModuleId] = path;
                        // Register built-in locales
                        _localizationService.RegisterModuleTranslations(
                            commandModule.ModuleId,
                            commandModule.DefaultTranslations);
                        _logger.LogInformation(
                            "Loaded command module: {ModuleName} v.{Version} with {CommandCount} commands",
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
            _logger.LogError(ex, "Failed to load command module from archive: {Path}", path);
            throw;
        }

        return modules;
    }

    public async Task UnloadModulesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Unloading command modules...");

        // S0: Shutdown all modules
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

        // S1: Unload all contexts
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

        // S2: Clearing temp
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

        // S3: Force GC, IDK if this is actually needed
        await Task.Run(() =>
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }, cancellationToken);

        _logger.LogInformation("Command modules unloaded");
    }

    public async Task<IReadOnlyList<ICommandModule>> ReloadModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        await _reloadLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Reloading command module: {ModuleId}", moduleId);

            // S0: Find module in loaded modules
            var existingModule = _loadedModules.FirstOrDefault(m => m.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
            string? archivePath = null;

            if (existingModule != null)
            {
                // S1: Get archive path from mapping
                if (!_moduleIdToArchivePath.TryGetValue(moduleId, out archivePath) || !File.Exists(archivePath))
                {
                    _logger.LogError("Module file not found for '{ModuleId}'", moduleId);
                    throw new FileNotFoundException($"Module file not found for '{moduleId}'");
                }

                _logger.LogDebug("Found existing module {ModuleId}: {ArchivePath}", moduleId, archivePath);

                // S2: Shutdown module
                await existingModule.ShutdownAsync();

                // S3: Find and unload the corresponding load context
                var contextToUnload = _loadContexts.FirstOrDefault(c => c.Name.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
                if (contextToUnload != null)
                {
                    contextToUnload.Unload();
                    _loadContexts.Remove(contextToUnload);
                    _logger.LogDebug("Unloaded context: {ContextName}", contextToUnload.Name);
                }

                // S4: Remove from loaded modules
                _loadedModules.RemoveAll(m => m.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));

                // S5: Remove temp directory
                var tempDirToDelete = _tempDirectories.FirstOrDefault(t => t.Contains(moduleId));
                if (!string.IsNullOrEmpty(tempDirToDelete) && Directory.Exists(tempDirToDelete))
                {
                    try
                    {
                        Directory.Delete(tempDirToDelete, recursive: true);
                        _tempDirectories.Remove(tempDirToDelete);
                        _logger.LogDebug("Deleted temp directory: {TempDir}", tempDirToDelete);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to delete temp directory: {TempDir}", tempDirToDelete);
                    }
                }

                // S6: Remove from mapping
                _moduleIdToArchivePath.Remove(moduleId);
            }
            else
            {
                // S1: Try to find archive by name
                var commandModulesPath = Path.Combine(_pathProvider.GetAppDataPath(), "Command");
                
                // S2: Try exact file name match
                var exactArchivePath = Path.Combine(commandModulesPath, $"{moduleId}.pag");
                if (File.Exists(exactArchivePath))
                {
                    archivePath = exactArchivePath;
                }
                else
                {
                    // S3: Try to find by manifest name
                    var moduleFiles = Directory.GetFiles(commandModulesPath, "*.pag", SearchOption.TopDirectoryOnly);
                    foreach (var file in moduleFiles)
                    {
                        var tempExtractDir = Path.Combine(Path.GetTempPath(), $"ButterBror_Command_{Guid.NewGuid()}");
                        try
                        {
                            ZipFile.ExtractToDirectory(file, tempExtractDir, overwriteFiles: true);
                            var manifestPath = Path.Combine(tempExtractDir, "command.manifest.json");
                            if (File.Exists(manifestPath))
                            {
                                var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
                                var manifest = JsonSerializer.Deserialize<CommandModuleManifest>(manifestJson);
                                if (manifest?.Name?.Equals(moduleId, StringComparison.OrdinalIgnoreCase) == true)
                                {
                                    archivePath = file;
                                    break;
                                }
                            }
                        }
                        catch
                        {
                            //
                        }
                        finally
                        {
                            if (Directory.Exists(tempExtractDir))
                            {
                                try { Directory.Delete(tempExtractDir, recursive: true); } catch { }
                            }
                        }
                    }
                }

                if (archivePath == null)
                {
                    _logger.LogError("Module '{ModuleId}' not found in loaded modules", moduleId);
                    throw new FileNotFoundException($"Module '{moduleId}' not found");
                }
            }

            // S7: Force GC
            await Task.Run(() =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }, cancellationToken);

            // S8: Load module
            _logger.LogDebug("Loading module: {Path}", archivePath);
            var newModules = await LoadModuleFromArchiveAsync(archivePath, cancellationToken);

            _logger.LogInformation("Reloaded command module '{ModuleId}': {Count} module(s) loaded", moduleId, newModules.Count);

            return newModules;
        }
        finally
        {
            _reloadLock.Release();
        }
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
                    // I DONT CARE
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
                    // Lol
                }
            }

            _tempDirectories.Clear();
        }

        _disposed = true;
    }
}