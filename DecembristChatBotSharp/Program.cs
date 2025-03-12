global using LanguageExt;
global using static LanguageExt.Prelude;
global using BotClient = Telegram.Bot.ITelegramBotClient;
using DecembristChatBotSharp;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

SetLogger.Do();
Log.Information("Starting bot");

var bot = Optional(Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")).Match(
    Some: token => new TelegramBotClient(token),
    None: () => throw new Exception("TELEGRAM_BOT_TOKEN is not set")
);

var receiverOptions = new ReceiverOptions
{
    AllowedUpdates =
    [
        UpdateType.Message,
        UpdateType.ChatMember,
        UpdateType.ChatJoinRequest
    ]
};

var appConfig = AppConfig.GetInstance().Match(
    Some: config => config,
    None: () => throw new Exception("failed to read appsettings.json")
);
var cancelToken = new CancellationTokenSource();
var db = new Database(appConfig);
var botHandler = new BotHandler(appConfig, bot, db);
var checkCaptchaScheduler = new CheckCaptchaScheduler(bot, appConfig, db);
bot.StartReceiving(botHandler, receiverOptions, cancelToken.Token);
checkCaptchaScheduler.Start(cancelToken.Token);

Console.CancelKeyPress += (_, args) =>
{
    Log.Information("Stopping bot");
    cancelToken.Cancel();
    args.Cancel = true;
};

Log.Information("Started bot");

await Task.Delay(Timeout.Infinite, cancelToken.Token);