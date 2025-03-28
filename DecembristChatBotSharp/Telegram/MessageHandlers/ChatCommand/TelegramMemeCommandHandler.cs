using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class TelegramMemeCommandHandler(
    AppConfig appConfig,
    MemberItemService memberItemService,
    AdminUserRepository adminUserRepository,
    MessageAssistance messageAssistance,
    BotClient botClient,
    ExpiredMessageRepository expiredMessageRepository,
    CancellationTokenSource cancelToken) : ICommandHandler
{
    public const string CommandKey = "/telegrammeme";

    public string Command => CommandKey;
    public string Description => "Generate random telegram meme from random channel";

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;

        var isAdmin = await adminUserRepository.IsAdmin(telegramId);

        var result = await memberItemService.UseTelegramMeme(chatId, telegramId, isAdmin);
        if (result.Result == UseTelegramMemeResult.Type.Failed)
        {
            result = await memberItemService.UseTelegramMeme(chatId, telegramId, isAdmin);
        }

        var (maybeMeme, resultType) = result;
        return resultType switch
        {
            UseTelegramMemeResult.Type.Failed => await SendTelegramErrorMessage(chatId),
            UseTelegramMemeResult.Type.NoItems => await messageAssistance.SendNoItems(chatId),
            UseTelegramMemeResult.Type.Success => await Array(
                TrySendMeme(chatId, maybeMeme),
                messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll(),
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

    private async Task<Unit> SendMeme(long chatId, TelegramRandomMeme meme) =>
        await botClient.SendPhoto(
                chatId, meme.PhotoLink, caption: "Украденный мем", cancellationToken: cancelToken.Token)
            .ToTryAsync()
            .Match(
                _ => Log.Information("Sent random telegram meme to chat {0}", chatId),
                ex => Log.Error(ex, "Failed to send random telegram meme {0} to chat {1}", meme.PhotoLink, chatId));

    private async Task<Unit> SendTelegramErrorMessage(long chatId)
    {
        var message = appConfig.CommandConfig.TelegramPostConfig.TelegramErrorMessage;
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