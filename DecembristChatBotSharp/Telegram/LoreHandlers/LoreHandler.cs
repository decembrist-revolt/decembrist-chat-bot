using DecembristChatBotSharp.Mongo;
using Lamar;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.LoreHandlers;

[Singleton]
public class LoreHandler(
    LoreMessageAssistant loreMessageAssistant,
    LoreDeleteHandler loreDeleteHandler,
    LoreContentHandler loreContentHandler,
    LoreKeyHandler loreKeyHandler,
    BotClient botClient,
    AppConfig appConfig,
    AdminUserRepository adminUserRepository,
    MessageAssistance messageAssistance,
    LoreUserRepository loreUserRepository,
    CancellationTokenSource cancelToken)
{
    public const string Tag = "#Lore";
    public const string KeySuffix = "Key";
    public const string ContentSuffix = "Content";
    public const string DeleteSuffix = "Delete";

    public async Task<TryAsync<Message>> Do(Message message)
    {
        var replyText = message.ReplyToMessage!.Text;
        var telegramId = message.From!.Id;
        var messageText = message.Text!;
        var dateReply = message.ReplyToMessage.Date;

        return TryAsync(await ParseReplyText(replyText).MatchAsync(
            None: () => loreMessageAssistant.SendHelpMessage(telegramId),
            Some: async tuple =>
            {
                await messageAssistance.DeleteCommandMessage(telegramId, message.ReplyToMessage.Id, Tag);
                await messageAssistance.DeleteCommandMessage(telegramId, message.Id, Tag);
                var (suffix, key, lorChatId) = tuple;
                var isEmpty = string.IsNullOrWhiteSpace(key);
                return suffix switch
                {
                    _ when !await IsLorUser(telegramId, lorChatId) => SendNotLoreUser(telegramId),
                    DeleteSuffix when isEmpty => loreDeleteHandler.Do(messageText, lorChatId, telegramId, dateReply),
                    KeySuffix when isEmpty => loreKeyHandler.Do(messageText, lorChatId, telegramId),
                    ContentSuffix => loreContentHandler.Do(key, messageText, lorChatId, telegramId, dateReply),
                    _ => loreMessageAssistant.SendHelpMessage(telegramId)
                };
            }));
    }

    private static Option<(string suffix, string Key, long LorChatId)> ParseReplyText(string replyText) =>
        replyText.Split(Tag) is [_, var keyAndId] &&
        keyAndId.Split(":") is [var suffix, var maybeKey, var idText] &&
        long.TryParse(idText, out var lorChatId)
            ? (suffix, maybeKey, lorChatId)
            : None;

    private async Task<bool> IsLorUser(long telegramId, long lorChatId) =>
        await loreUserRepository.IsLoreUser((telegramId, lorChatId))
        || await adminUserRepository.IsAdmin((telegramId, lorChatId));

    private Task<Message> SendNotLoreUser(long telegramId) =>
        botClient.SendMessage(telegramId, appConfig.LoreConfig.NotLoreUser, cancellationToken: cancelToken.Token);
}