using DecembristChatBotSharp.Entity.Configs;
using DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;
using Lamar;
using Telegram.Bot.Types.ReplyMarkups;

namespace DecembristChatBotSharp.Service.Buttons;

[Singleton]
public class CaptchaButtons(Random random)
{
    private const int Attempts = 5;

    public InlineKeyboardMarkup GetMarkup(long telegramId, CaptchaConfig captchaConfig)
    {
        var correctAnswer = captchaConfig.CaptchaAnswer;
        var keyboardButtons = new List<InlineKeyboardButton[]>
        {
            new[] { GetCaptchaButton(correctAnswer, telegramId) },
            new[] { GetCaptchaButton(GetWrongAnswer(correctAnswer), telegramId) },
            new[] { GetCaptchaButton(GetWrongAnswer(correctAnswer), telegramId) },
        };

        var captchaButtons = keyboardButtons
            .OrderBy(_ => random.Next())
            .ToArray();

        return new InlineKeyboardMarkup(captchaButtons);
    }

    private string GetWrongAnswer(string correctAnswer)
    {
        var chars = correctAnswer.ToCharArray();
        var attempts = 0;
        string wrongAnswer;
        do
        {
            attempts++;
            wrongAnswer = new string(chars.OrderBy(_ => random.Next()).ToArray());
        } while (wrongAnswer == correctAnswer && attempts < Attempts);

        return wrongAnswer == correctAnswer ? wrongAnswer + Attempts : wrongAnswer;
    }

    private static InlineKeyboardButton GetCaptchaButton(string name, long telegramId)
    {
        var callback = CallbackService.GetCallback(
            CaptchaCallbackHandler.PrefixKey, name, (CallbackService.UserIdParameter, telegramId));
        return InlineKeyboardButton.WithCallbackData(name, callback);
    }
}