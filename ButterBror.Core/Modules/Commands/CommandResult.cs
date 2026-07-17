using ButterBror.Core.Messaging;

namespace ButterBror.Core.Modules.Commands;

public class CommandResult
{
    public bool Success { get; set; }
    public Message? Message { get; set; }
    public object? Data { get; set; }
    public bool SendResult { get; set; }
    public TimeSpan ExecutionTime { get; set; }

    public static CommandResult Successfully(string message = "Command executed successfully", object? data = null, bool sendResult = true) =>
        new() { Success = true, Message = new Message(message), Data = data, SendResult = sendResult };

    public static CommandResult Failure(string message, Exception? exception = null, bool sendResult = true) =>
        new() { Success = false, Message = new Message(message), SendResult = sendResult, Data = exception };
    
    public static CommandResult Successfully(Message message, object? data = null, bool sendResult = true) =>
        new() { Success = true, Message = message, Data = data, SendResult = sendResult };

    public static CommandResult Failure(Message message, Exception? exception = null, bool sendResult = true) =>
        new() { Success = false, Message = message, SendResult = sendResult, Data = exception };
}