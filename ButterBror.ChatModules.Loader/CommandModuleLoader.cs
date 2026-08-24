using System.Collections.Concurrent;
using System.IO.Compression;
using System.Runtime.Loader;
using System.Text.Json;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Modules;
using ButterBror.Core.Modules.Manifest;
using ButterBror.Core.Scopes;
using Microsoft.Extensions.Logging;

namespace ButterBror.Modules.Loader;

/// <summary>
/// Loader for dynamic command modules
/// </summary>
public sealed class CommandModuleLoader(
    IAppDataPathProvider pathProvider,
    IServiceProvider serviceProvider,
    ILogger<CommandModuleLoader> logger,
    ILocalizationService localizationService)
    : IDisposable, ICommandModuleLoader
{
    private readonly List<AssemblyLoadContext> _loadContexts = [];
    private readonly List<ICommandModule> _loadedModules = [];
    private readonly ConcurrentBag<string> _tempDirectories = [];
    private readonly Dictionary<string, string> _moduleIdToArchivePath = new();
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private bool _disposed;

    private const string ManifestFileName = "bbmanifest.json";
    private readonly string _tempPath = Path.Combine(Path.GetTempPath(), "bb", "cmd");

    public async Task<IReadOnlyList<ICommandModule>> LoadModulesAsync(CancellationToken ct = default)
    {
        _loadedModules.Clear();
        _loadContexts.Clear();

        var commandsPath = Path.Combine(pathProvider.GetAppDataPath(), "Command");
        
        if (!Directory.Exists(commandsPath))
        {
            Directory.CreateDirectory(commandsPath);
            return [];
        }

        await using (new InitializationScope(logger, "command modules"))
        {
            // Looking for archives with command modules
            var moduleFiles = Directory.GetFiles(commandsPath, "*.pag", SearchOption.TopDirectoryOnly);

            if (moduleFiles.Length == 0)
            {
                logger.LogInformation("no command modules found");
                return [];
            }

            var tasks = moduleFiles.Select(file => LoadModuleFromArchiveAsync(file, ct));
            var results = await Task.WhenAll(tasks);

            var allModules = results.SelectMany(r => r).ToList();
            _loadedModules.AddRange(allModules);

            logger.LogInformation("loaded command modules. count={Count}", _loadedModules.Count);
            return _loadedModules.AsReadOnly();
        }
    }

    private async Task<IReadOnlyList<ICommandModule>> LoadModuleFromArchiveAsync(string path, CancellationToken cancellationToken)
    {
        var modules = new List<ICommandModule>();

        var tempDir = Path.Combine(_tempPath, Guid.CreateVersion7().ToString());
        Directory.CreateDirectory(tempDir);
        _tempDirectories.Add(tempDir);

        try
        {
            logger.LogDebug("extracting command module archive: {Path} to {TempDir}", path, tempDir);
            await ZipFile.ExtractToDirectoryAsync(path, tempDir, overwriteFiles: true, cancellationToken: cancellationToken);

            // s0: reading manifest
            var manifestPath = Path.Combine(tempDir, ManifestFileName);
            CommandModuleManifest? manifest = null;
            
            if (File.Exists(manifestPath))
            {
                var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
                manifest = JsonSerializer.Deserialize<CommandModuleManifest>(manifestJson);
                logger.LogDebug("loaded manifest: {ManifestName} v.{ManifestVersion}", manifest?.Id, manifest?.Version);
            }

            // s1: finding main dll
            string? mainDll = null;
            if (manifest != null && !string.IsNullOrWhiteSpace(manifest.MainDll))
            {
                mainDll = Path.Combine(tempDir, manifest.MainDll);
                if (!File.Exists(mainDll))
                {
                    logger.LogWarning("main dll from manifest not found: {Dll}", manifest.MainDll);
                    mainDll = null;
                }
            }

            if (mainDll == null)
            {
                logger.LogWarning("no module dll found in archive: {Path}", path);
                return modules;
            }

            logger.LogDebug("found module. name='{Dll}'", mainDll);

            // s2: creating isolated context
            var moduleName = manifest?.Id ?? Path.GetFileNameWithoutExtension(path);
            var loadContext = new ModuleAssemblyLoadContext(moduleName, tempDir, isCollectible: true, logger);
            _loadContexts.Add(loadContext);

            // s3: loading assembly
            var assembly = loadContext.LoadFromAssemblyPath(mainDll);
            logger.LogDebug("loaded assembly. name='{AssemblyName}'", assembly.FullName);

            // s4: finding all classes that implement ICommandModule
            var moduleTypes = assembly.GetTypes()
                .Where(t => typeof(ICommandModule).IsAssignableFrom(t)
                            && t is { IsInterface: false, IsAbstract: false })
                .ToList();

            foreach (var moduleType in moduleTypes)
            {
                try
                {
                    var module = Activator.CreateInstance(moduleType);
                    if (module is ICommandModule commandModule)
                    {
                        await commandModule.InitializeAsync(serviceProvider);
                        modules.Add(commandModule);
                        // store mapping from module ID to archive path
                        _moduleIdToArchivePath[commandModule.ModuleId] = path;
                        // register built-in locales
                        localizationService.RegisterModuleTranslations(
                            commandModule.ModuleId,
                            commandModule.DefaultTranslations);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to create instance of module type: {TypeName}", moduleType.Name);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "failed to load command module from archive: {Path}", path);
        }

        return modules;
    }

    public async Task UnloadModulesAsync(CancellationToken cancellationToken = default)
    {
        await using var _ = new StopScope(logger, "command modules");
        
        // s0: shutdown all modules
        foreach (var module in _loadedModules)
        {
            try
            {
                await module.ShutdownAsync();
                logger.LogDebug("[shutdown] {ModuleId}", module.ModuleId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "failed to shutdown module: {ModuleId}", module.ModuleId);
            }
        }

        // s1: unload all contexts
        foreach (var context in _loadContexts)
        {
            try
            {
                context.Unload();
                logger.LogDebug("unloaded context: {ContextName}", context.Name);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "failed to unload context: {ContextName}", context.Name);
            }
        }

        _loadContexts.Clear();
        _loadedModules.Clear();

        // s2: clearing temp
        foreach (var tempDir in _tempDirectories)
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                    logger.LogDebug("[del] {TempDir}", tempDir);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "failed to delete temp directory: {TempDir}", tempDir);
            }
        }

        _tempDirectories.Clear();

        // s3: force GC
        await Task.Run(() =>
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<ICommandModule>> ReloadModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        await _reloadLock.WaitAsync(cancellationToken);
        try
        {
            // s0: find module in loaded modules
            var existingModule = _loadedModules.FirstOrDefault(m => m.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
            string? archivePath = null;

            if (existingModule != null)
            {
                // s1: get archive path from mapping
                if (!_moduleIdToArchivePath.TryGetValue(moduleId, out archivePath) || !File.Exists(archivePath))
                {
                    throw new FileNotFoundException($"module file not found for '{moduleId}'");
                }

                logger.LogDebug("found existing module {ModuleId}: {ArchivePath}", moduleId, archivePath);

                // s2: shutdown module
                await existingModule.ShutdownAsync();

                // s3: find and unload the corresponding load context
                var contextToUnload = _loadContexts.FirstOrDefault(_ =>
                    _loadedModules.Any(m => m.ModuleId == moduleId));
                if (contextToUnload != null)
                {
                    contextToUnload.Unload();
                    _loadContexts.Remove(contextToUnload);
                    logger.LogDebug("unloaded context: {ContextName}", contextToUnload.Name);
                }

                // s4: remove from loaded modules
                _loadedModules.RemoveAll(m => m.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));

                // s5: remove temp directory
                var tempDirToDelete = _tempDirectories.FirstOrDefault(t => t.Contains(moduleId));
                if (!string.IsNullOrEmpty(tempDirToDelete) && Directory.Exists(tempDirToDelete))
                {
                    try
                    {
                        Directory.Delete(tempDirToDelete, recursive: true);
                        logger.LogDebug("[del] {TempDir}", tempDirToDelete);
                    }
                    catch
                    {
                        // ok?
                    }
                }

                // s6: remove from mapping
                _moduleIdToArchivePath.Remove(moduleId);
            }
            else
            {
                // s1: try to find archive by name
                var commandModulesPath = Path.Combine(pathProvider.GetAppDataPath(), "Command");
                
                // s2: try exact file name match
                var exactArchivePath = Path.Combine(commandModulesPath, $"{moduleId}.pag");
                if (File.Exists(exactArchivePath))
                {
                    archivePath = exactArchivePath;
                }
                else
                {
                    // s3: try to find by manifest name
                    var moduleFiles = Directory.GetFiles(commandModulesPath, "*.pag", SearchOption.TopDirectoryOnly);
                    foreach (var file in moduleFiles)
                    {
                        var tempExtractDir = Path.Combine(_tempPath, Guid.CreateVersion7().ToString());
                        try
                        {
                            await ZipFile.ExtractToDirectoryAsync(file, tempExtractDir, overwriteFiles: true, cancellationToken: cancellationToken);
                            var manifestPath = Path.Combine(tempExtractDir, ManifestFileName);
                            if (File.Exists(manifestPath))
                            {
                                var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
                                var manifest = JsonSerializer.Deserialize<CommandModuleManifest>(manifestJson);
                                if (manifest?.Id.Equals(moduleId, StringComparison.OrdinalIgnoreCase) == true)
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
                                try { Directory.Delete(tempExtractDir, recursive: true); }
                                catch
                                {
                                    // ignored
                                }
                            }
                        }
                    }
                }

                if (archivePath == null)
                {
                    logger.LogError("module '{ModuleId}' not found", moduleId);
                    throw new FileNotFoundException($"module '{moduleId}' not found");
                }
            }

            // s7: force GC
            await Task.Run(() =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }, cancellationToken);

            // s8: load module
            logger.LogDebug("loading module: {Path}", archivePath);
            var newModules = await LoadModuleFromArchiveAsync(archivePath, cancellationToken);
            _loadedModules.AddRange(newModules);

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
    }

    private void Dispose(bool disposing)
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
                    // shtup
                    logger.LogError(ex, "failed to unload context: {ContextName}", context.Name);
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
                    // * v *
                }
            }

            _tempDirectories.Clear();
        }

        _disposed = true;
    }
}