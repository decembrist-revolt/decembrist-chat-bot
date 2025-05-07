using DecembristChatBotSharp.Telegram.MessageHandlers;
using Lamar;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Telegram.CallbackHandlers;

[Singleton]
public class PrivateCallbackHandler(
    BotClient botClient,
    ProfileCallbackHandler profileCallbackHandler,
    MessageAssistance messageAssistance,
    CancellationTokenSource cancelToken)
{
    public async Task<Unit> Do(CallbackQuery callbackQuery)
    {
        await botClient.AnswerCallbackQuery(callbackQuery.Id);

        var data = callbackQuery.Data;
        var telegramId = callbackQuery.From.Id;

        var maybeCallback = ParseCallback(data!);
        return await maybeCallback.MatchAsync(
            None: () => messageAssistance.SendCommandResponse(telegramId, "OK", nameof(PrivateCallbackHandler)),
            Some: callback =>
            {
                var (prefix, suffix, targetChatId) = callback;
                return prefix switch
                {
                    ProfileCallbackHandler.Prefix => profileCallbackHandler.Do(suffix, targetChatId, callbackQuery),
                    _ => messageAssistance.SendCommandResponse(telegramId, "OK", nameof(PrivateCallbackHandler))
                };
            });
    }

    private Option<(string prefix, string suffix, long chatId)> ParseCallback(string callback) =>
        callback.Split(PrivateMessageHandler.SplitSymbol) is [var prefix, var suffix, var chatIdText] &&
        long.TryParse(chatIdText, out var chatId)
            ? (prefix, suffix, chatId)
            : None;
}