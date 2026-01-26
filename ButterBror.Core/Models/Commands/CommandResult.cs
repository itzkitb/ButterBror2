namespace ButterBror.Core.Models.Commands;

public class CommandResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public object? Data { get; set; }
    public TimeSpan ExecutionTime { get; set; }

    public static CommandResult Successfully(string message = "Command executed successfully", object? data = null) =>
        new() { Success = true, Message = message, Data = data };

    public static CommandResult Failure(string message, Exception? exception = null) =>
        new() { Success = false, Message = message };
}