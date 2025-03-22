using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;
using Lamar;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Telegram.Bot;

namespace DecembristChatBotSharp.DI;

public class DiContainer
{
    public const string BOT_TELEGRAM_ID = "BotTelegramId";

    public static async Task<Container> GetInstance(CancellationTokenSource cancellationTokenSource)
    {
        var registry = new ServiceRegistry();

        registry.AddSingleton(cancellationTokenSource);

        var appConfig = GetAppConfig();
        registry.AddSingleton(appConfig);

        var botClient = new TelegramBotClient(appConfig.TelegramBotToken);
        registry.AddSingleton<BotClient>(botClient);

        var botTelegramId = await GetBotTelegramId(botClient, cancellationTokenSource.Token);
        registry.AddKeyedSingleton<Func<long>>(BOT_TELEGRAM_ID, () => botTelegramId);
        
        registry.Scan(s =>
        {
            s.TheCallingAssembly();
            s.WithDefaultConventions();
            s.AddAllTypesOf<ICommandHandler>();
            s.AddAllTypesOf<IRepository>();
        });

        return new Container(registry);
    }

    private static AppConfig GetAppConfig() => AppConfig.GetInstance().Match(
        identity,
        () => throw new Exception("failed to read appsettings.json")
    );

    private static Task<long> GetBotTelegramId(TelegramBotClient botClient, CancellationToken cancelToken)
    {
        return TryAsync(botClient.GetMe(cancelToken))
            .Map(botUser =>
            {
                Log.Information("Bot is authorized");
                return botUser.Id;
            }).IfFail(IfFail);

        long IfFail(Exception ex)
        {
            Log.Error(ex, "Failed to get bot user");
            throw ex;
        }
    }
}