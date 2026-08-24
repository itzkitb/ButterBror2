using ButterBror.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Runtime.Loader;
using System.Text.Json;
using ButterBror.Core.Modules;
using ButterBror.Core.Modules.Interfaces;
using ButterBror.Core.Modules.Manifest;
using ButterBror.Core.Scopes;

namespace ButterBror.Modules.Loader;

/// <summary>
/// Loader for dynamic chat modules
/// </summary>
public sealed class ChatModuleLoader(
    IAppDataPathProvider pathProvider,
    IServiceProvider serviceProvider,
    ILogger<ChatModuleLoader> logger,
    ILocalizationService localizationService)
    : IChatModuleLoader, IDisposable
{
    private readonly List<AssemblyLoadContext> _loadContexts = [];
    private readonly List<IChatModule> _loadedModules = [];
    private readonly ConcurrentBag<string> _tempDirectories = [];
    private readonly Dictionary<string, string> _moduleToArchivePath = new();
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private bool _disposed;

    private const string ManifestFileName = "bbmanifest.json";
    private readonly string _tempPath = Path.Combine(Path.GetTempPath(), "bb", "chat");

    public async Task<IReadOnlyList<IChatModule>> LoadModulesAsync(CancellationToken ct = default)
    {
        _loadedModules.Clear();
        _loadContexts.Clear();

        var chatsPath = Path.Combine(pathProvider.GetAppDataPath(), "Chat");

        if (!Directory.Exists(chatsPath))
        {
            Directory.CreateDirectory(chatsPath);
            return [];
        }

        await using (new InitializationScope(logger, "chat modules"))
        {
            // s0: looking for archives with modules
            var moduleFiles = Directory.GetFiles(chatsPath, "*.pag", SearchOption.TopDirectoryOnly);

            if (moduleFiles.Length == 0)
            {
                logger.LogInformation("no chat modules found");
                return [];
            }

            var tasks = moduleFiles.Select(file => LoadModuleFromArchiveAsync(file, ct));
            var results = await Task.WhenAll(tasks);

            var allModules = results.SelectMany(r => r).ToList();
            _loadedModules.AddRange(allModules);
            
            logger.LogInformation("loaded chat modules. count={Count}", _loadedModules.Count);
            return _loadedModules.AsReadOnly();
        }
    }

    private async Task<IReadOnlyList<IChatModule>> LoadModuleFromArchiveAsync(string path, CancellationToken cancellationToken)
    {
        var modules = new List<IChatModule>();

        // s0: create a temporary directory
        var tempDir = Path.Combine(_tempPath, Guid.CreateVersion7().ToString());
        Directory.CreateDirectory(tempDir);
        _tempDirectories.Add(tempDir);

        try
        {
            logger.LogDebug("extracting module archive. path='{Path}', to='{TempDir}'", path, tempDir);

            await ZipFile.ExtractToDirectoryAsync(path, tempDir, overwriteFiles: true, cancellationToken: cancellationToken);

            // s1: reading the manifest
            var manifestPath = Path.Combine(tempDir, ManifestFileName);
            ChatModuleManifest? manifest = null;

            if (File.Exists(manifestPath))
            {
                var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
                manifest = JsonSerializer.Deserialize<ChatModuleManifest>(manifestJson);
                logger.LogDebug("loaded manifest. name='{ManifestName}', version='{ManifestVersion}'", manifest?.Name, manifest?.Version);
            }

            // s2: defining the Master DLL
            string? mainDll = null;

            if (manifest != null && !string.IsNullOrWhiteSpace(manifest.MainDll))
            {
                mainDll = Path.Combine(tempDir, manifest.MainDll);
                if (!File.Exists(mainDll))
                {
                    logger.LogWarning("dll from manifest not found. name='{Dll}'", manifest.MainDll);
                    mainDll = null;
                }
            }

            if (mainDll == null)
            {
                logger.LogWarning("no module dll found in archive. path='{Path}'", path);
                return modules;
            }

            logger.LogDebug("found module. name='{Dll}'", mainDll);

            // s3: creating a context for isolation
            var moduleName = manifest?.Name ?? Path.GetFileNameWithoutExtension(path);
            var loadContext = new ModuleAssemblyLoadContext(moduleName, tempDir, isCollectible: true, logger);
            _loadContexts.Add(loadContext);

            // s4: loading the main assembly
            var assembly = loadContext.LoadFromAssemblyPath(mainDll);
            logger.LogDebug("loaded assembly. name='{AssemblyName}'", assembly.FullName);

            // s5: looking for all classes that implement IChatModule
            var moduleTypes = assembly.GetTypes()
                .Where(t => typeof(IChatModule).IsAssignableFrom(t)
                            && t is { IsInterface: false, IsAbstract: false })
                .ToList();

            foreach (var moduleType in moduleTypes)
            {
                try
                {
                    // s6: create an instance
                    var module = Activator.CreateInstance(moduleType);
                    if (module is IChatModule chatModule)
                    {
                        await chatModule.InitializeAsync(serviceProvider);
                        modules.Add(chatModule);
                        // s7: store mapping from module platform name to archive path
                        _moduleToArchivePath[chatModule.ModuleId] = path;
                        // s8: register built-in locales
                        localizationService.RegisterModuleTranslations(
                            chatModule.ModuleId,
                            chatModule.DefaultTranslations);
                        logger.LogInformation(
                            "loaded chat module. name='{ModuleName}', id='{PlatformName}', v={Version}",
                            moduleType.Name,
                            chatModule.ModuleId,
                            chatModule.Version
                        );
                    }
                    else
                    {
                        logger.LogWarning("type does not implement ichatmodule. name='{TypeName}'", moduleType.Name);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "failed to create instance of module type. name='{TypeName}'", moduleType.Name);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "failed to load module from archive. path='{Path}'", path);
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
        
        // s1: unloading contexts
        foreach (var context in _loadContexts)
        {
            try
            {
                context.Unload();
                logger.LogDebug("unloaded context. name='{ContextName}'", context.Name);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "failed to unload context. name='{ContextName}'", context.Name);
            }
        }

        _loadContexts.Clear();
        _loadedModules.Clear();

        // s2: clear temp
        foreach (var tempDir in _tempDirectories)
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                    logger.LogDebug("deleted temp directory. path='{TempDir}'", tempDir);
                }
            }
            catch
            {
                // ok :)
            }
        }

        _tempDirectories.Clear();

        // s3: force GC to free memory
        await Task.Run(() =>
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<IChatModule>> ReloadModuleAsync(string moduleId, CancellationToken cancellationToken = default)
    {
        await _reloadLock.WaitAsync(cancellationToken);
        try
        {
            logger.LogInformation("reloading chat module. name='{ModuleName}'", moduleId);

            // s0: find module in loaded
            var existingModule = _loadedModules.FirstOrDefault(m => m.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
            string? archivePath = null;

            if (existingModule != null)
            {
                // s1: get archive path from mapping
                if (!_moduleToArchivePath.TryGetValue(moduleId, out archivePath) || !File.Exists(archivePath))
                {
                    logger.LogError("new module file not found. name='{ModuleName}'", moduleId);
                    throw new FileNotFoundException($"module file not found for '{moduleId}'");
                }

                logger.LogDebug("found existing module. name='{ModuleName}', path='{ArchivePath}'", moduleId, archivePath);

                // s2: shutdown module
                await existingModule.ShutdownAsync();
                logger.LogDebug("shutdown module. name='{ModuleName}'", moduleId);

                // s3: find and unload the corresponding load context
                var contextToUnload = _loadContexts.FirstOrDefault(_ =>
                    _loadedModules.Any(m => m.ModuleId == moduleId));
                if (contextToUnload != null)
                {
                    contextToUnload.Unload();
                    _loadContexts.Remove(contextToUnload);
                    logger.LogDebug("unloaded context. name='{ContextName}'", contextToUnload.Name);
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
                        logger.LogDebug("deleted temp directory. path='{TempDir}'", tempDirToDelete);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "failed to delete temp directory. path='{TempDir}'", tempDirToDelete);
                    }
                }

                // s6: remove from mapping
                _moduleToArchivePath.Remove(moduleId);
            }
            else
            {
                // s1: try to find archive by name
                var chatModulesPath = Path.Combine(pathProvider.GetAppDataPath(), "Chat");
                
                // s2: first try exact file name match
                var exactArchivePath = Path.Combine(chatModulesPath, $"{moduleId}.pag");
                if (File.Exists(exactArchivePath))
                {
                    archivePath = exactArchivePath;
                }
                else
                {
                    // s3: try to find by manifest name
                    var moduleFiles = Directory.GetFiles(chatModulesPath, "*.pag", SearchOption.TopDirectoryOnly);
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
                                var manifest = JsonSerializer.Deserialize<ChatModuleManifest>(manifestJson);
                                if (manifest?.Name.Equals(moduleId, StringComparison.OrdinalIgnoreCase) == true)
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
                    logger.LogError("module not found in loaded modules or files. path='{ModuleName}'", moduleId);
                    throw new FileNotFoundException($"module '{moduleId}' not found");
                }
            }

            // s7: force GC
            await Task.Run(() =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }, cancellationToken);

            // s8: load module from archive
            logger.LogDebug("loading module from archive. path='{Path}'", archivePath);
            var newModules = await LoadModuleFromArchiveAsync(archivePath, cancellationToken);
            _loadedModules.AddRange(newModules);

            logger.LogInformation(
                "reloaded chat module. name='{ModuleName}', version={Version}",
                moduleId,
                newModules.FirstOrDefault()?.Version);

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
                    logger.LogError(ex, "module unload error. name='{ContextName}'", context.Name);
                }
            }
            _loadContexts.Clear();
            _loadedModules.Clear();

            // clearing temporary directories
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
                    //
                }
            }
            _tempDirectories.Clear();
        }

        _disposed = true;
    }
}

