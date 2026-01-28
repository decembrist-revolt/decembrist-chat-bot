using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
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
    ChatConfigService chatConfigService,
    AdminUserRepository adminUserRepository,
    MessageAssistance messageAssistance,
    LoreUserRepository loreUserRepository,
    CancellationTokenSource cancelToken)
{
    public const string Tag = "#Lore";
    public const string KeySuffix = "Key";
    public const string ContentSuffix = "Content";
    public const string DeleteSuffix = "Delete";

    public async Task<Message> Do(Message message)
    {
        var replyText = message.ReplyToMessage!.Text;
        var telegramId = message.From!.Id;
        var messageText = message.Text!;
        var dateReply = message.ReplyToMessage.Date;

        return await ParseReplyText(replyText).Match(
            None: async () => await loreMessageAssistant.SendNotAvailableMessage(telegramId),
            Some: async tuple =>
            {
                await messageAssistance.DeleteCommandMessage(telegramId, message.ReplyToMessage.Id, Tag);
                await messageAssistance.DeleteCommandMessage(telegramId, message.Id, Tag);
                var (suffix, key, lorChatId) = tuple;
                var isEmpty = string.IsNullOrWhiteSpace(key);

                var maybeLoreConfig = await chatConfigService.GetConfig(lorChatId, config => config.LoreConfig);
                if (!maybeLoreConfig.TryGetSome(out var loreConfig))
                {
                    var sendNotLoreUser = await SendNotLoreUser(telegramId, "Lore config not found");
                    return chatConfigService.LogNonExistConfig(sendNotLoreUser, nameof(LoreConfig));
                }

                return suffix switch
                {
                    _ when !await IsLorUser(telegramId, lorChatId) => await SendNotLoreUser(telegramId,
                        loreConfig.NotLoreUser),
                    DeleteSuffix when isEmpty => await loreDeleteHandler.Do(messageText, lorChatId, telegramId,
                        dateReply, loreConfig),
                    KeySuffix when isEmpty => await loreKeyHandler.Do(messageText, lorChatId, telegramId, loreConfig),
                    ContentSuffix => await loreContentHandler.Do(key, messageText, lorChatId, telegramId, dateReply,
                        loreConfig),
                    _ => await loreMessageAssistant.SendHelpMessage(telegramId, loreConfig)
                };
            });
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

    private Task<Message> SendNotLoreUser(long telegramId, string message) =>
        botClient.SendMessage(telegramId, message, cancellationToken: cancelToken.Token);
}