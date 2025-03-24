using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Service;
using Lamar;
using Serilog;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class GiveItemCommandHandler(
    BotClient botClient,
    MemberItemService memberItemService,
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

        var maybeUsername = maybeReplyTelegramId.Map(async replyTelegramId =>
        {
            if (await memberItemService.GiveItem(chatId, replyTelegramId, telegramId, itemType))
            {
                return await botClient.GetUsername(chatId, replyTelegramId, cancelToken.Token);
            }
            
            return None;
        }).MapAsync(TaskOptionAsyncExtensions.ToAsync).Flatten();

        var sendGetItemMessageTask = maybeUsername
            .IfSome(username => messageAssistance.SendGetItemMessage(chatId, username, itemType));

        return await Array(
            sendGetItemMessageTask,
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
    }
}