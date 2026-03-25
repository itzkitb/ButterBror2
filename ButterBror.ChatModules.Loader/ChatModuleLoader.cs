using System.IO.Compression;
using System.Runtime.Loader;
using System.Text.Json;
using ButterBror.ChatModule;
using ButterBror.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ButterBror.ChatModules.Loader;

/// <summary>
/// Implementing a CML
/// </summary>
public class ChatModuleLoader : IChatModuleLoader, IDisposable
{
    private readonly IAppDataPathProvider _pathProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ChatModuleLoader> _logger;
    private readonly List<AssemblyLoadContext> _loadContexts = new();
    private readonly List<IChatModule> _loadedModules = new();
    private readonly List<string> _tempDirectories = new();
    private readonly Dictionary<string, string> _moduleToZipPath = new();
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private bool _disposed;

    private const string ManifestFileName = "module.manifest.json";

    public ChatModuleLoader(
        IAppDataPathProvider pathProvider,
        IServiceProvider serviceProvider,
        ILogger<ChatModuleLoader> logger)
    {
        _pathProvider = pathProvider;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<IChatModule>> LoadModulesAsync(CancellationToken cancellationToken = default)
    {
        _loadedModules.Clear();
        _loadContexts.Clear();

        var chatModulesPath = Path.Combine(_pathProvider.GetAppDataPath(), "Chat");

        if (!Directory.Exists(chatModulesPath))
        {
            _logger.LogInformation("Chat modules directory does not exist: {Path}. Creating...", chatModulesPath);
            Directory.CreateDirectory(chatModulesPath);
            return Array.Empty<IChatModule>();
        }

        _logger.LogInformation("Loading chat modules from: {Path}", chatModulesPath);

        // Looking for archives with modules
        var moduleFiles = Directory.GetFiles(chatModulesPath, "*.zip", SearchOption.TopDirectoryOnly);

        if (moduleFiles.Length == 0)
        {
            _logger.LogInformation("No chat modules (ZIP archives) found in {Path}", chatModulesPath);
            return Array.Empty<IChatModule>();
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
                _logger.LogError(ex, "Failed to load module from: {ZipFile}", zipFile);
            }
        }

        _logger.LogInformation("Loaded {Count} chat module(s)", _loadedModules.Count);
        return _loadedModules.AsReadOnly();
    }

    private async Task<IReadOnlyList<IChatModule>> LoadModuleFromZipAsync(string zipPath, CancellationToken cancellationToken)
    {
        var modules = new List<IChatModule>();

        // Create a temporary directory
        var tempDir = Path.Combine(Path.GetTempPath(), $"ButterBror_Module_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        _tempDirectories.Add(tempDir);

        try
        {
            _logger.LogDebug("Extracting module archive: {ZipPath} to {TempDir}", zipPath, tempDir);

            ZipFile.ExtractToDirectory(zipPath, tempDir, overwriteFiles: true);

            // Reading the manifesto
            var manifestPath = Path.Combine(tempDir, ManifestFileName);
            ModuleManifest? manifest = null;

            if (File.Exists(manifestPath))
            {
                var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
                manifest = JsonSerializer.Deserialize<ModuleManifest>(manifestJson);
                _logger.LogDebug("Loaded manifest: {ManifestName} v{ManifestVersion}", manifest?.Name, manifest?.Version);
            }

            // Defining the Master DLL
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
                _logger.LogWarning("No module DLL found in archive: {ZipPath}", zipPath);
                return modules;
            }

            _logger.LogDebug("Found main module DLL: {Dll}", mainDll);

            // Creating a New Boot Context for Isolation
            var moduleName = manifest?.Name ?? Path.GetFileNameWithoutExtension(zipPath);
            var loadContext = new ModuleAssemblyLoadContext(moduleName, tempDir, isCollectible: true, _logger);
            _loadContexts.Add(loadContext);

            // Loading the main assembly
            var assembly = loadContext.LoadFromAssemblyPath(mainDll);
            _logger.LogDebug("Loaded assembly: {AssemblyName}", assembly.FullName);

            // Looking for all classes that implement IChatModule
            var moduleTypes = assembly.GetTypes()
                .Where(t => typeof(IChatModule).IsAssignableFrom(t)
                         && !t.IsInterface
                         && !t.IsAbstract)
                .ToList();

            foreach (var moduleType in moduleTypes)
            {
                try
                {
                    // Create an instance
                    var module = Activator.CreateInstance(moduleType);
                    if (module is IChatModule chatModule)
                    {
                        chatModule.InitializeWithServices(_serviceProvider);
                        modules.Add(chatModule);
                        // Store mapping from module platform name to zip path
                        _moduleToZipPath[chatModule.PlatformName] = zipPath;
                        _logger.LogInformation(
                            "Loaded chat module: {ModuleName} (Platform: {PlatformName})",
                            moduleType.Name,
                            chatModule.PlatformName
                        );
                    }
                    else
                    {
                        _logger.LogWarning("Type {TypeName} does not implement IChatModule", moduleType.Name);
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
            _logger.LogError(ex, "Failed to load module from archive: {ZipPath}", zipPath);
            throw;
        }

        return modules;
    }

    public async Task UnloadModulesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Unloading chat modules...");

        // Unloading all contexts
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

        _logger.LogInformation("Chat modules unloaded");
    }

    public async Task<IReadOnlyList<IChatModule>> ReloadModuleAsync(string moduleName, CancellationToken cancellationToken = default)
    {
        await _reloadLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Reloading chat module: {ModuleName}", moduleName);

            // Find module in loaded modules
            var existingModule = _loadedModules.FirstOrDefault(m => m.PlatformName.Equals(moduleName, StringComparison.OrdinalIgnoreCase));
            string? zipPath = null;

            if (existingModule != null)
            {
                // Get zip path from mapping
                if (!_moduleToZipPath.TryGetValue(moduleName, out zipPath) || !File.Exists(zipPath))
                {
                    _logger.LogError("Module ZIP file not found for '{ModuleName}'", moduleName);
                    throw new FileNotFoundException($"Module ZIP file not found for '{moduleName}'");
                }

                _logger.LogDebug("Found existing module {ModuleName} with ZIP: {ZipPath}", moduleName, zipPath);

                // Shutdown module
                await existingModule.ShutdownAsync();
                _logger.LogDebug("Shutdown module: {ModuleName}", moduleName);

                // Find and unload the corresponding load context
                var contextToUnload = _loadContexts.FirstOrDefault(c => c.Name.Equals(moduleName, StringComparison.OrdinalIgnoreCase));
                if (contextToUnload != null)
                {
                    contextToUnload.Unload();
                    _loadContexts.Remove(contextToUnload);
                    _logger.LogDebug("Unloaded context: {ContextName}", contextToUnload.Name);
                }

                // Remove from loaded modules
                _loadedModules.RemoveAll(m => m.PlatformName.Equals(moduleName, StringComparison.OrdinalIgnoreCase));

                // Remove temp directory
                var tempDirToDelete = _tempDirectories.FirstOrDefault(t => t.Contains(moduleName));
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

                // Remove from mapping
                _moduleToZipPath.Remove(moduleName);
            }
            else
            {
                // Module not found in memory - try to find ZIP by name
                var chatModulesPath = Path.Combine(_pathProvider.GetAppDataPath(), "Chat");
                
                // First try exact file name match
                var exactZipPath = Path.Combine(chatModulesPath, $"{moduleName}.zip");
                if (File.Exists(exactZipPath))
                {
                    zipPath = exactZipPath;
                }
                else
                {
                    // Try to find by manifest name
                    var moduleFiles = Directory.GetFiles(chatModulesPath, "*.zip", SearchOption.TopDirectoryOnly);
                    foreach (var file in moduleFiles)
                    {
                        var tempExtractDir = Path.Combine(Path.GetTempPath(), $"ButterBror_Module_{Guid.NewGuid()}");
                        try
                        {
                            ZipFile.ExtractToDirectory(file, tempExtractDir, overwriteFiles: true);
                            var manifestPath = Path.Combine(tempExtractDir, ManifestFileName);
                            if (File.Exists(manifestPath))
                            {
                                var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
                                var manifest = JsonSerializer.Deserialize<ModuleManifest>(manifestJson);
                                if (manifest?.Name?.Equals(moduleName, StringComparison.OrdinalIgnoreCase) == true)
                                {
                                    zipPath = file;
                                    break;
                                }
                            }
                        }
                        catch
                        {
                            // Ignore extraction errors during search
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

                if (zipPath == null)
                {
                    _logger.LogError("Module '{ModuleName}' not found in loaded modules or ZIP files", moduleName);
                    throw new FileNotFoundException($"Module '{moduleName}' not found");
                }
            }

            // Force GC to free memory
            await Task.Run(() =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }, cancellationToken);

            // Load module from ZIP
            _logger.LogDebug("Loading module from ZIP: {ZipPath}", zipPath);
            var newModules = await LoadModuleFromZipAsync(zipPath, cancellationToken);

            _logger.LogInformation("Reloaded chat module '{ModuleName}': {Count} module(s) loaded", moduleName, newModules.Count);

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
                    // Fck off
                    _logger.LogError($"{context.Name} module unload error", ex);
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
                    }
                }
                catch
                {
                    // shit
                }
            }
            _tempDirectories.Clear();
        }

        _disposed = true;
    }
}

