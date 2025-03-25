using System.Text.RegularExpressions;
using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using Lamar;
using LanguageExt.UnsafeValueAccess;
using Serilog;
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

        return await db.GetRestrictMember(new RestrictMember.CompositeId(telegramId, chatId))
            .MatchAsync(
                async restrictMember =>
                {
                    var result =
                        _restrictTypeHandler.HandleRestrict(parameters.Payload, restrictMember.RestrictType);
                    if (!result) return false;
                    await botClient.DeleteMessageAndLog(chatId, messageId,
                        () => Log.Information("Deleted restrict message in chat {0}, user {1}", chatId, telegramId),
                        ex => Log.Error(ex, "Failed to delete restrict message in chat {0},user {1}", chatId,
                            telegramId)
                    );
                    return true;
                },
                () => false);
    }

    private class RestrictTypeHandler
    {
        private readonly Dictionary<RestrictType, Func<IMessagePayload, bool>> _handlers;

        public RestrictTypeHandler()
        {
            _handlers = new Dictionary<RestrictType, Func<IMessagePayload, bool>>()
            {
                { RestrictType.Link, HandleLink },
                { RestrictType.Emoji, HandleEmoji }
            };
        }

        public bool HandleRestrict(IMessagePayload payload, RestrictType restrictType)
        {
            var result = restrictType == RestrictType.All;
            if (!result)
                foreach (var (flag, handler) in _handlers)
                {
                    if ((restrictType & flag) != flag) continue;
                    result |= handler(payload);
                    if (result)
                        break;
                }

            return result;
        }

        private bool HandleEmoji(IMessagePayload payload) => false;

        private bool HandleLink(IMessagePayload payload)
        {
            var regex = new Regex(@"https?:\/\/[^\s]+");
            if (payload is TextPayload textPayload)
                return regex.Match(textPayload.Text).Success;
            return false;
        }
    }
}