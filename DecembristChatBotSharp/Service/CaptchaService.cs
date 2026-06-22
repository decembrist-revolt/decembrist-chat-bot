namespace DecembristChatBotSharp.Service;

public class CaptchaService(Random random)
{
    const int Attempts = 5;

    public string GetWrongAnswer(string correctAnswer)
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
}