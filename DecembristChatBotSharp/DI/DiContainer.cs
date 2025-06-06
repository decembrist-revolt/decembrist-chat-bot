﻿using DecembristChatBotSharp.Items;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Scheduler;
using DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;
using DecembristChatBotSharp.Telegram.CallbackHandlers.PrivateCallback;
using DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;
using Lamar;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace DecembristChatBotSharp.DI;

public class DiContainer
{
    public static Container GetInstance(CancellationTokenSource cancellationTokenSource)
    {
        var registry = new ServiceRegistry();

        registry.AddSingleton(cancellationTokenSource);

        var appConfig = GetAppConfig();
        registry.AddSingleton(appConfig);
        
        registry.AddHttpClients(appConfig);
        registry.AddTelegram();

        var mongoUrl = new MongoUrl(appConfig.MongoConfig.ConnectionString);
        registry.AddSingleton(mongoUrl);
        var client = new MongoClient(appConfig.MongoConfig.ConnectionString);
        registry.AddSingleton(client);

        registry.AddQuartz();
        registry.AddSingleton<Random>();

        registry.Scan(s =>
        {
            s.TheCallingAssembly();
            s.WithDefaultConventions();
            s.AddAllTypesOf<ICommandHandler>();
            s.AddAllTypesOf<IPrivateCallbackHandler>();
            s.AddAllTypesOf<IPassiveItem>();
            s.AddAllTypesOf<IChatCallbackHandler>();
            s.AddAllTypesOf<IRepository>();
            s.AddAllTypesOf<IRegisterJob>();
        });

        return new Container(registry);
    }

    private static AppConfig GetAppConfig() => AppConfig.GetInstance().Match(
        identity,
        () => throw new Exception("failed to read appsettings.json")
    );
}