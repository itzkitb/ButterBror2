namespace ButterBror.Core.Modules;

/// <summary>
/// Describes loaded module status
/// </summary>
public record ModuleInfo(
    string ModuleId,
    Version? Version,
    string Kind,
    bool IsLoaded,
    bool IsEnabled,
    int ExportedCommandsCount
);
