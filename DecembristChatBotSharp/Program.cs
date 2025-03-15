global using LanguageExt;
global using static LanguageExt.Prelude;
global using BotClient = Telegram.Bot.ITelegramBotClient;
using DecembristChatBotSharp;
using DecembristChatBotSharp.Telegram;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

SetLogger.Do();
Log.Information("Starting bot");

var cancelTokenSource = new CancellationTokenSource();

try
{
    var container = await DiContainer.GetInstance(cancelTokenSource);
    Log.Information("DI Container created");
    var botHandler = container.GetRequiredService<BotHandler>();
    botHandler.Start();
    var checkCaptcha = container.GetRequiredService<CheckCaptchaScheduler>();
    checkCaptcha.Start();

    Console.CancelKeyPress += (_, args) =>
    {
        CancelGlobalToken();
        args.Cancel = true;
    };

    AppDomain.CurrentDomain.ProcessExit += (_, _) => CancelGlobalToken();

    Log.Information("Bot started");
}
catch
{
    CancelGlobalToken(1);
    throw;
}

await Task.Delay(Timeout.Infinite, cancelTokenSource.Token);

return;

void CancelGlobalToken(int statusCode = 0)
{
    Log.Warning("Stopping bot {0}", statusCode);
    cancelTokenSource.Cancel();
}