using Serilog;
using Serilog.Events;

namespace DecembristChatBotSharp;

public static class SetLogger
{
    private const string LogTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

    public static Unit Do()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(outputTemplate: LogTemplate)
            .WriteTo.File(
                path: $"logs/log-error-{DateTime.Now:yyyy-MM-dd}.log",
                outputTemplate: LogTemplate,
                rollingInterval: RollingInterval.Day,
                restrictedToMinimumLevel: LogEventLevel.Error,
                retainedFileCountLimit: 31)
            .WriteTo.Async(a => a.File(
                path: $"logs/log-{DateTime.Now:yyyy-MM-dd}.log",
                outputTemplate: LogTemplate,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31))
            .CreateLogger();
        return unit;
    }
}