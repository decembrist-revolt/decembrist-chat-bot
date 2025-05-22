using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Service;
using Lamar;
using LanguageExt.UnsafeValueAccess;
using Serilog;

namespace DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;

[Singleton]
public class GiveItemCommandHandler(
    BotClient botClient,
    MemberItemService memberItemService,
    MessageAssistance messageAssistance,
    CancellationTokenSource cancelToken) : ICommandHandler
{
    public string Command => "/give";
    public string Description => $"Give item to replied member, options: {_itemOptions}";
    public CommandLevel CommandLevel => CommandLevel.Admin;

    private readonly string _itemOptions =
        string.Join(", ", Enum.GetValues<MemberItemType>().Map(type => type.ToString()));

    public async Task<Unit> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;
        var maybeReplyTelegramId = parameters.ReplyToTelegramId;

        if (parameters.Payload is not TextPayload { Text: var text }) return unit;

        if (maybeReplyTelegramId.IsNone)
        {
            Log.Warning("Admin {0} in chat {1} try to give item without reply", telegramId, chatId);
            return unit;
        }

        if (text.Trim().Split(' ') is not [_, var itemAndCount])
        {
            Log.Warning("Admin {0} in chat {1} try to give item without item", telegramId, chatId);
            return unit;
        }

        var maybeItem = memberItemService.ParseItem(itemAndCount);
        if (maybeItem.IsNone)
        {
            Log.Warning("Admin {0} in chat {1} try to give invalid item {2}", telegramId, chatId, itemAndCount);
            return unit;
        }

        var (item, quantity) = maybeItem.ValueUnsafe();

        var maybeUsername = maybeReplyTelegramId.Map(async replyTelegramId =>
        {
            if (await memberItemService.GiveItem(chatId, replyTelegramId, telegramId, item, quantity))
            {
                return await botClient.GetUsername(chatId, replyTelegramId, cancelToken.Token);
            }

            return None;
        }).MapAsync(TaskOptionAsyncExtensions.ToAsync).Flatten();

        var sendGetItemMessageTask = maybeUsername
            .IfSome(username => messageAssistance.SendGetItemMessage(chatId, username, item, quantity));

        return await Array(
            sendGetItemMessageTask,
            messageAssistance.DeleteCommandMessage(chatId, messageId, Command)).WhenAll();
    }
}