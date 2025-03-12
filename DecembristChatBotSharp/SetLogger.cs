using Serilog;

namespace DecembristChatBotSharp;

public static class SetLogger
{
    private const string LogTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

    public static Unit Do()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: LogTemplate)
            .WriteTo.File(
                path: $"logs/log-{DateTime.Now:yyyy-MM-dd}.log",
                outputTemplate: LogTemplate,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31)
            .CreateLogger();
        return unit;
    }
}