using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Reddit;
using DecembristChatBotSharp.Telegram;
using DecembristChatBotSharp.Telegram.MessageHandlers;
using DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;
using JasperFx.Core.TypeScanning;
using Lamar;
using Lamar.Scanning.Conventions;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Telegram.Bot;

namespace DecembristChatBotSharp;

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

        registry.AddSingleton<RedditService>();
        registry.AddSingleton<ExpiredMessageService>();

        registry.AddSingleton<MongoDatabase>();
        registry.Scan(s =>
        {
            s.TheCallingAssembly();
            //для регистрации по I[Name] [Name] s.WithDefaultConventions(lifetime: ServiceLifetime.Singleton);

            s.Convention<SingletonConvention<IRepository>>();

            registry.AddSingleton<ICommandHandler, ShowLikesCommandHandler>();
            s.Convention<SingletonConvention<ICommandHandler>>();
        });

        registry.AddSingleton<BotHandler>();
        registry.AddSingleton<CheckCaptchaScheduler>();
        registry.AddSingleton<NewMemberHandler>();
        registry.AddSingleton<PrivateMessageHandler>();
        registry.AddSingleton<FastReplyHandler>();
        registry.AddSingleton<CaptchaHandler>();
        registry.AddSingleton<ChatCommandHandler>();
        registry.AddSingleton<ChatMessageHandler>();
        registry.AddSingleton<ChatBotAddHandler>();
        registry.AddSingleton<WrongCommandHandler>();

        registry.AddSingleton(sp =>
            new Lazy<List<ICommandHandler>>(() => [..sp.GetServices<ICommandHandler>()]));
        registry.AddSingleton<MessageAssistance>();

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

public class SingletonConvention<T> : IRegistrationConvention
{
    public void ScanTypes(TypeSet types, ServiceRegistry registry)
    {
        var serviceTypes = types.FindTypes(TypeClassification.Concretes | TypeClassification.Closed)
            .Where(t => t.GetInterfaces().Contains(typeof(T)));

        foreach (var type in serviceTypes)
        {
            registry.For(typeof(T)).Use(type).Singleton();
            registry.For(type).Use(type).Singleton();
        }
    }
}