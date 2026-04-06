using DecembristChatBotSharp.Telegram.CallbackHandlers.ChatCallback;
using Lamar;
using Telegram.Bot.Types.ReplyMarkups;

namespace DecembristChatBotSharp.Service.Buttons;

[Singleton]
public class CaptchaButtons(Random random)
{
    private const int Attempts = 5;

    public InlineKeyboardMarkup GetMarkup(long telegramId, string correctAnswer)
    {
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
        bool isEqual;
        do
        {
            attempts++;
            for (var i = chars.Length - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (chars[i], chars[j]) = (chars[j], chars[i]);
            }

            wrongAnswer = new string(chars);
            isEqual = wrongAnswer == correctAnswer;
        } while (isEqual && attempts < Attempts);

        return isEqual ? wrongAnswer + Attempts : wrongAnswer;
    }

    private static InlineKeyboardButton GetCaptchaButton(string name, long telegramId)
    {
        var callback = CallbackService.GetCallback(
            CaptchaCallbackHandler.PrefixKey, name, (CallbackService.UserIdParameter, telegramId));
        return InlineKeyboardButton.WithCallbackData(name, callback);
    }
}