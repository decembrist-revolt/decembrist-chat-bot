using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Telegram;
using DecembristChatBotSharp.Telegram.MessageHandlers;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Telegram.Bot;

namespace DecembristChatBotSharp;

public class DiContainer
{
    public const string BOT_TELEGRAM_ID = "BotTelegramId";

    public static async Task<ServiceProvider> GetInstance(CancellationTokenSource cancellationTokenSource)
    {
        var services = new ServiceCollection();
        
        services.AddSingleton(cancellationTokenSource);

        var appConfig = GetAppConfig();
        services.AddSingleton(appConfig);

        var botClient = new TelegramBotClient(appConfig.TelegramBotToken);
        services.AddSingleton<BotClient>(botClient);

        var botTelegramId = await GetBotTelegramId(botClient, cancellationTokenSource.Token);
        services.AddKeyedSingleton<Func<long>>(BOT_TELEGRAM_ID, () => botTelegramId);

        services.AddSingleton<MongoDatabase>();
        services.AddSingleton<NewMemberRepository>();
        
        services.AddSingleton<BotHandler>();
        services.AddSingleton<CheckCaptchaScheduler>();
        services.AddSingleton<NewMemberHandler>();
        services.AddSingleton<PrivateMessageHandler>();
        services.AddSingleton<FastReplyHandler>();
        services.AddSingleton<CaptchaHandler>();
        services.AddSingleton<ChatMessageHandler>();
        services.AddSingleton<ChatBotAddHandler>();

        return services.BuildServiceProvider();
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