using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using LanguageExt.UnsafeValueAccess;
using Serilog;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class GiveItemCommandHandler(
    BotClient botClient,
    MemberItemRepository memberItemRepository,
    AdminUserRepository adminUserRepository,
    MessageAssistance messageAssistance,
    CancellationTokenSource cancelToken) : ICommandHandler
{
    public string Command => "/give";
    public string Description => $"Give item to replied member, options: {_itemOptions}";

    private readonly string _itemOptions =
        string.Join(", ", Enum.GetValues<MemberItemType>().Map(type => type.ToString()));

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var chatId = parameters.ChatId;
        var telegramId = parameters.TelegramId;
        var messageId = parameters.MessageId;
        var maybeReplyTelegramId = parameters.ReplyToTelegramId;

        if (parameters.Payload is not TextPayload { Text: var text }) return unit;

        if (!await adminUserRepository.IsAdmin(telegramId))
        {
            return await messageAssistance.SendAdminOnlyMessage(chatId, telegramId);
        }

        if (maybeReplyTelegramId.IsNone)
        {
            Log.Warning("Admin {0} in chat {1} try to give item without reply", telegramId, chatId);
            return unit;
        }

        var replyTelegramId = maybeReplyTelegramId.ValueUnsafe();

        if (text.Trim().Split(' ') is not [_, var item])
        {
            Log.Warning("Admin {0} in chat {1} try to give item without item", telegramId, chatId);
            return unit;
        }

        if (!(Enum.TryParse(typeof(MemberItemType), item, true, out var type) && type is MemberItemType itemType))
        {
            Log.Warning("Admin {0} in chat {1} try to give invalid item {2}", telegramId, chatId, item);
            return unit;
        }

        return await AddMemberItem(chatId, replyTelegramId, itemType, messageId);
    }

    private async Task<Unit> AddMemberItem(long chatId, long telegramId, MemberItemType itemType, int messageId)
    {
        if (await memberItemRepository.AddMemberItem(telegramId, chatId, itemType))
        {
            Log.Information("Admin add item {0} to {1} in chat {2}", itemType, telegramId, chatId);
            var deleteTask = messageAssistance.DeleteCommandMessage(chatId, messageId, Command);

            var maybeUsername = await botClient.GetUsername(chatId, telegramId, cancelToken.Token);
            var sendMessageTask = maybeUsername
                .MapAsync(username => messageAssistance.SendGetItemMessage(chatId, username, itemType))
                .Match(_ => unit, () => unit);

            return await Array(deleteTask, sendMessageTask).WhenAll();
        }

        Log.Error("Failed admin add item {0} to {1} in chat {2}", itemType, telegramId, chatId);
        return unit;
    }
}