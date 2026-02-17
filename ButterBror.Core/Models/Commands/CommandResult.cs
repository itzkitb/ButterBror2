namespace ButterBror.Core.Models.Commands;

public class CommandResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public object? Data { get; set; }
    public bool SendResult { get; set; }
    public TimeSpan ExecutionTime { get; set; }

    public static CommandResult Successfully(string message = "Command executed successfully", object? data = null, bool sendResult = true) =>
        new() { Success = true, Message = message, Data = data, SendResult = sendResult };

    public static CommandResult Failure(string message, Exception? exception = null, bool sendResult = true) =>
        new() { Success = false, Message = message, SendResult = sendResult };
}