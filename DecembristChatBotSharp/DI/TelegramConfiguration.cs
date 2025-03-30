using Lamar;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.DI;

public static class TelegramConfiguration
{
    public static void AddTelegram(this ServiceRegistry registry)
    {
        registry.AddSingleton<BotClient>(sp =>
        {
            var appConfig = sp.GetRequiredService<AppConfig>();
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
            var cancelToken = sp.GetRequiredService<CancellationTokenSource>();
            
            return new TelegramBotClient(appConfig.TelegramBotToken, httpClient, cancelToken.Token);
        });

        registry.AddSingleton(sp =>
        {
            var botClient = sp.GetRequiredService<BotClient>();
            var cancelToken = sp.GetRequiredService<CancellationTokenSource>();

            var getUserTask =
                from botUser in botClient.GetMe(cancelToken.Token).ToTryAsync()
                    .Do(_ => Log.Information("Bot is authorized"))
                    .IfFail(IfFail)
                select botUser;

            return getUserTask.Result;

            User IfFail(Exception ex)
            {
                Log.Error(ex, "Failed to get bot user");
                throw ex;
            }
        });
    }
}