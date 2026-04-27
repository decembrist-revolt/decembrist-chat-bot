using DecembristChatBotSharp.DI;
using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class TelegramMemeCommandHandler(
    AppConfig appConfig,
    MemberItemService memberItemService,
    AdminUserRepository adminUserRepository,
    MessageAssistance messageAssistance,
    BotClient botClient,
    ExpiredMessageRepository expiredMessageRepository,
    ChatConfigService chatConfigService,
    MemeDownloadService memeDownloadService,
    CancellationTokenSource cancelToken) : ICommandHandler
{
    public const string CommandKey = "/telegrammeme";
    private const string StolenMemeCaption = "Украденный мем";

    public string Command => CommandKey;

    public string Description =>
        appConfig.CommandAssistanceConfig.CommandDescriptions.GetValueOrDefault(CommandKey,
            "Generate random telegram meme from random channel");

    public CommandLevel CommandLevel => CommandLevel.Item;

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;

        var maybeCommandConfig = await chatConfigService.GetConfig(chatId, config => config.TelegramPostConfig);
        if (!maybeCommandConfig.TryGetSome(out var telegramPostConfig))
        {
            await messageAssistance.SendNotConfigured(chatId, messageId, Command);
            return chatConfigService.LogNonExistConfig(unit, nameof(TelegramPostConfig), Command);
        }

        var isAdmin = await adminUserRepository.IsAdmin(new(telegramId, chatId));

        var result = await memberItemService.UseTelegramMeme(chatId, telegramId, isAdmin);
        if (result.Result == UseTelegramMemeResult.Type.Failed)
        {
            result = await memberItemService.UseTelegramMeme(chatId, telegramId, isAdmin);
        }

        var (maybeMeme, resultType) = result;
        await messageAssistance.DeleteCommandMessage(chatId, messageId, Command);
        return resultType switch
        {
            UseTelegramMemeResult.Type.Failed => await SendTelegramErrorMessage(chatId, telegramPostConfig),
            UseTelegramMemeResult.Type.NoItems => await messageAssistance.SendNoItems(chatId),
            UseTelegramMemeResult.Type.Success => await TrySendMeme(chatId, maybeMeme),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private Task<Unit> TrySendMeme(long chatId, Option<TelegramRandomMeme> maybeMeme)
    {
        var maybeSend =
            from meme in maybeMeme
            select SendMeme(chatId, meme);

        return maybeSend.ToAsync().IfSome(identity);
    }

    private async Task<Unit> SendMeme(long chatId, TelegramRandomMeme meme)
    {
        await memeDownloadService.SendMeme(
            chatId,
            meme.PhotoLink,
            HttpClientConfiguration.TelegramMemeClient,
            StolenMemeCaption,
            Command);
        return unit;
    }

    private async Task<Unit> SendTelegramErrorMessage(long chatId, TelegramPostConfig telegramPostConfig)
    {
        var message = telegramPostConfig.TelegramErrorMessage;
        return await botClient.SendMessageAndLog(chatId, message,
            message =>
            {
                Log.Information("Sent reddit error message to chat {0}", chatId);
                expiredMessageRepository.QueueMessage(chatId, message.MessageId);
            },
            ex => Log.Error(ex, "Failed to send reddit error message to chat {0}", chatId),
            cancelToken.Token);
    }
}