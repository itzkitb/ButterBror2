namespace ButterBror.Dashboard.Models;

public record DeleteRequest(string? Path);
public record CreateDirectoryRequest(string? Path);
public record RenameRequest(string? Path, string? NewName);
