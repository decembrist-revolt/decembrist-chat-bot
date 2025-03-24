using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using Telegram.Bot;

namespace DecembristChatBotSharp.Telegram.MessageHandlers;

[Singleton]
public class RestrictHandler(
    BotClient botClient,
    RestrictRepository db,
    CancellationTokenSource cancelToken
)
{
    private RestrictTypeHandler _restrictTypeHandler = new();

    public async Task<bool> Do(ChatMessageHandlerParams parameters)
    {
        var (messageId, telegramId, chatId) = parameters;

        if (!await db.IsRestricted(new RestrictMember.CompositeId(telegramId, chatId))) return false;

        var restrictMember = await db.GetMember(new RestrictMember(new RestrictMember.CompositeId(telegramId, chatId),
            RestrictType.All));

        var result = await _restrictTypeHandler.HandleRestrict(parameters.Payload, restrictMember.RestrictType);
        if (!result) return false;

        await botClient.DeleteMessage(chatId, messageId, cancelToken.Token);
        return true;
    }

    private class RestrictTypeHandler
    {
        private readonly Dictionary<RestrictType, Func<IMessagePayload, Task<bool>>> _handlers;

        public RestrictTypeHandler()
        {
            _handlers = new Dictionary<RestrictType, Func<IMessagePayload, Task<bool>>>()
            {
                { RestrictType.Link, HandleLink },
                { RestrictType.Sticker, HandleSticker },
                { RestrictType.Emoji, HandleEmoji },
                { RestrictType.Text, HandleText }
            };
        }

        public async Task<bool> HandleRestrict(IMessagePayload payload, RestrictType restrictType)
        {
            var result = restrictType == RestrictType.All;
            if (!result)
                foreach (var (flag, handler) in _handlers)
                {
                    if ((restrictType & flag) != flag) continue;
                    result |= await handler(payload);
                    if (result)
                        break;
                }

            return result;
        }

        private async Task<bool> HandleText(IMessagePayload payload) => payload is TextPayload;
        private async Task<bool> HandleSticker(IMessagePayload payload) => payload is StickerPayload;
        private async Task<bool> HandleEmoji(IMessagePayload payload) => false;

        private async Task<bool> HandleLink(IMessagePayload payload)
        {
            var regex = new Regex(@"https?:\/\/[^\s]+");
            if (payload is TextPayload textPayload)
                return regex.Match(textPayload.Text).Success;
            return false;
        }
    }
}