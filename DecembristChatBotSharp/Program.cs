﻿global using LanguageExt;
global using static LanguageExt.Prelude;
global using BotClient = Telegram.Bot.ITelegramBotClient;
using DecembristChatBotSharp;
using DecembristChatBotSharp.DI;
using DecembristChatBotSharp.Mongo;
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
    await container.GetRequiredService<MongoDatabase>().EnsureIndexes();
    Log.Information("Indexes ensured");
    var botHandler = container.GetRequiredService<BotHandler>();
    botHandler.Start();
    var checkCaptcha = container.GetRequiredService<CheckCaptchaScheduler>();
    checkCaptcha.Start();
    var expiredMessageService = container.GetRequiredService<ExpiredMessageService>();
    expiredMessageService.Start();
    // Log.Information(container.WhatDidIScan());
    // Log.Information(container.WhatDoIHave());

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