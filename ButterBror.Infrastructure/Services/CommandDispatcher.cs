using System.Security.Cryptography;
using System.Text;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Messaging;
using ButterBror.Core.Modules.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ButterBror.Infrastructure.Services;

public class CommandDispatcher(
    ILogger<CommandDispatcher> logger,
    ICommandRegistry commandRegistry,
    IServiceProvider provider,
    IErrorTrackingService errorTrackingService)
    : ICommandDispatcher
{
    private readonly IDashboardBridge _dashboardBridge = provider.GetRequiredService<IDashboardBridge>();

    public async Task<CommandResult> DispatchAsync(CommandContext context)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // S0: Obtaining the command factory from the registry
            var factory = commandRegistry.GetCommandFactory(context.CommandName, true);
            if (factory == null)
            {
                return CommandResult.Failure($"Command not found. name='{context.CommandName}'", sendResult: false);
            }

            // S1: Create a command instance through a factory
            var command = factory();

            // S2: Create an execution context and service provider
            var serviceProvider = new CommandServiceProvider(provider);

            var result = await command.ExecuteAsync(context, serviceProvider);
            result.ExecutionTime = stopwatch.Elapsed;

            // Notify dashboard about executed command
            _dashboardBridge.IncrementCommandCount();

            return result;
        }
        catch (Exception ex)
        {
            var errorHash = GenerateExceptionHash(ex);
            logger.LogError(ex,
                "Error dispatching command. name='{CommandName}', uid='{UserId}', error_code='{ErrorCode}'",
                context.CommandName, context.PlatformUser.Id, errorHash);

            errorTrackingService.LogError(ex, ex.Message, context);

            return new CommandResult
            {
                Success = false,
                Message = new Message(
                    $"🚨 | An internal error has occurred ▹ The developers are already aware of it ▹ Error code: {errorHash}"),
                ExecutionTime = stopwatch.Elapsed,
                SendResult = true
            };
        }
        finally
        {
            stopwatch.Stop();
        }
    }
    
    public static string GenerateExceptionHash(Exception ex)
    {
        // S0. Receive class
        var targetMethod = ex.TargetSite;
        string className = targetMethod?.DeclaringType?.Name ?? "UnknownClass";

        // S1. Generating an abbreviation
        string abbreviation = GetPascalCaseAbbreviation(className);

        // S2. Calculate a hash
        string input = $"{ex.GetType().FullName}\n{ex.StackTrace}";
        using var sha256 = SHA256.Create();
        byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        string hash = Convert.ToHexString(bytes)[..8];

        // S3. Final
        return $"{abbreviation}:{hash}";
    }

    private static string GetPascalCaseAbbreviation(string input)
    {
        if (string.IsNullOrEmpty(input)) return "UNK";
        
        string cleanName = new string(input.Where(char.IsLetterOrDigit).ToArray());
        var upperLetters = cleanName.Where(char.IsUpper).ToArray();

        if (upperLetters.Length > 0)
        {
            return new string(upperLetters);
        }
        
        return cleanName.Length >= 3 ? cleanName[..3].ToUpper() : cleanName.ToUpper();
    }
}
