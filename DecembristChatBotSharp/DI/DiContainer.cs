﻿using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Scheduler;
using DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;
using Lamar;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.DI;

public class DiContainer
{
    public static async Task<Container> GetInstance(CancellationTokenSource cancellationTokenSource)
    {
        var registry = new ServiceRegistry();

        registry.AddSingleton(cancellationTokenSource);

        var appConfig = GetAppConfig();
        registry.AddSingleton(appConfig);

        var botClient = new TelegramBotClient(appConfig.TelegramBotToken);
        registry.AddSingleton<BotClient>(botClient);

        var botUser = await GetBotUser(botClient, cancellationTokenSource.Token);
        registry.AddSingleton(botUser);

        var mongoUrl = new MongoUrl(appConfig.MongoConfig.ConnectionString);
        registry.AddSingleton(mongoUrl);
        var client = new MongoClient(appConfig.MongoConfig.ConnectionString);
        registry.AddSingleton(client);

        registry.AddQuartz();
        registry.AddHttpClients(appConfig);
        registry.AddSingleton<Random>();

        registry.Scan(s =>
        {
            s.TheCallingAssembly();
            s.WithDefaultConventions();
            s.AddAllTypesOf<ICommandHandler>();
            s.AddAllTypesOf<IRepository>();
            s.AddAllTypesOf<IRegisterJob>();
        });

        return new Container(registry);
    }

    private static AppConfig GetAppConfig() => AppConfig.GetInstance().Match(
        identity,
        () => throw new Exception("failed to read appsettings.json")
    );

    private static async Task<User> GetBotUser(TelegramBotClient botClient, CancellationToken cancelToken)
    {
        return await 
            from botUser in botClient.GetMe(cancelToken).ToTryAsync()
                .Do(_ => Log.Information("Bot is authorized"))
                .IfFail(IfFail)
            select botUser;

        User IfFail(Exception ex)
        {
            Log.Error(ex, "Failed to get bot user");
            throw ex;
        }
    }
}