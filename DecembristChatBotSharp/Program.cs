global using LanguageExt;
global using static LanguageExt.Prelude;
global using BotClient = Telegram.Bot.ITelegramBotClient;
using DecembristChatBotSharp;
using DecembristChatBotSharp.DI;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Scheduler;
using DecembristChatBotSharp.Telegram;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

SetLogger.Do();
Log.Information("Starting bot");

var cancelTokenSource = new CancellationTokenSource();

try
{
    var container = DiContainer.GetInstance(cancelTokenSource);
    Log.Information("DI Container created");
    var mongoDatabase = container.GetRequiredService<MongoDatabase>();
    await mongoDatabase.CheckConnection();
    await mongoDatabase.EnsureIndexes();
    Log.Information("Indexes ensured");
    var botHandler = container.GetRequiredService<BotHandler>();
    await botHandler.RegisterTipsCommand();
    botHandler.Start();
    var jobManager = container.GetRequiredService<JobManager>();
    await jobManager.Start();
    // Log.Information(container.WhatDidIScan());
    // Log.Information(container.WhatDoIHave());

    Console.CancelKeyPress += (_, args) =>
    {
        jobManager.Shutdown().Wait();
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