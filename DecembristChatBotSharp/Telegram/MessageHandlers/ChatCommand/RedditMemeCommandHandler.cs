using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;
using Telegram.Bot.Types.Enums;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class RedditMemeCommandHandler(
    AppConfig appConfig,
    MemberItemService memberItemService,
    AdminUserRepository adminUserRepository,
    MessageAssistance messageAssistance,
    BotClient botClient,
    ExpiredMessageRepository expiredMessageRepository,
    CancellationTokenSource cancelToken) : ICommandHandler
{
    public const string CommandKey = "/redditmeme";

    public string Command => CommandKey;
    public string Description => "Generate random reddit meme";
    public CommandLevel CommandLevel => CommandLevel.User;

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var telegramId = parameters.TelegramId;
        var chatId = parameters.ChatId;
        var messageId = parameters.MessageId;

        var isAdmin = await adminUserRepository.IsAdmin(new(telegramId, chatId));

        var result = await memberItemService.UseRedditMeme(chatId, telegramId, isAdmin);
        if (result.Result == UseRedditMemeResult.Type.Failed)
        {
            result = await memberItemService.UseRedditMeme(chatId, telegramId, isAdmin);
        }

        var (maybeMeme, resultType) = result;
        return resultType switch
        {
            UseRedditMemeResult.Type.Failed => await SendRedditErrorMessage(chatId),
            UseRedditMemeResult.Type.NoItems => await messageAssistance.SendNoItems(chatId),
            UseRedditMemeResult.Type.Success => await Array(
                TrySendMeme(chatId, maybeMeme, messageId),
                messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll(),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private Task<Unit> TrySendMeme(long chatId, Option<RedditRandomMeme> maybeMeme, int messageId)
    {
        var maybeSend =
            from meme in maybeMeme
            select SendMeme(chatId, meme);

        return maybeSend.ToAsync().IfSome(identity);
    }

    private async Task<Unit> SendMeme(long chatId, RedditRandomMeme meme)
    {
        var message = $"{meme.Url.EscapeMarkdown()}\n[Источник]({meme.Permalink.EscapeMarkdown()})";
        return await botClient.SendMessageAndLog(chatId, message, ParseMode.MarkdownV2,
            _ => Log.Information("Sent random meme to chat {0}", chatId),
            ex => Log.Error(ex, "Failed to send random meme {0} to chat {1}", message, chatId),
            cancelToken.Token);
    }

    private async Task<Unit> SendRedditErrorMessage(long chatId)
    {
        var message = appConfig.RedditConfig.RedditErrorMessage;
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