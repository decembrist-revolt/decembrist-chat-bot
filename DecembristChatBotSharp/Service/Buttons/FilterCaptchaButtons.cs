using DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;
using Lamar;
using Telegram.Bot.Types.ReplyMarkups;

namespace DecembristChatBotSharp.Service.Buttons;

[Singleton]
public class FilterCaptchaButtons(Random random, CaptchaService captchaService)
{
    public InlineKeyboardMarkup GetMarkup(long telegramId, string correctAnswer,
        string callbackPrefix = CaptchaCallbackHandler.PrefixKey)
    {
        var keyboardButtons = new List<InlineKeyboardButton[]>
        {
            new[] { GetCaptchaCorrectButton(correctAnswer, telegramId) },
            new[] { GetCaptchaWrongButton(captchaService.GetWrongAnswer(correctAnswer), telegramId) },
            new[] { GetCaptchaWrongButton(captchaService.GetWrongAnswer(correctAnswer), telegramId) },
        };

        var captchaButtons = keyboardButtons
            .OrderBy(_ => random.Next())
            .ToArray();

        return new InlineKeyboardMarkup(captchaButtons);
    }

    private static InlineKeyboardButton GetCaptchaCorrectButton(string name, long telegramId)
    {
        var callback = CallbackService.GetCallback(FilterCaptchaCallbackHandler.PrefixKey, FilterCaptchaResult.Correct,
            (CallbackService.UserIdParameter, telegramId));
        return InlineKeyboardButton.WithCallbackData(name, callback);
    }

    private static InlineKeyboardButton GetCaptchaWrongButton(string name, long telegramId)
    {
        var callback = CallbackService.GetCallback(FilterCaptchaCallbackHandler.PrefixKey, FilterCaptchaResult.Wrong,
            (CallbackService.UserIdParameter, telegramId));
        return InlineKeyboardButton.WithCallbackData(name, callback);
    }
}