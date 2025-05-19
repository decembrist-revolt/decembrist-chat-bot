using System.Text;
using DecembristChatBotSharp.Telegram;
using Lamar;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class CallbackService(MessageAssistance messageAssistance)
{
    private const char ParamSeparator = '=';
    private const char PathSeparator = '/';
    public const string ChatIdParameter = "chatId";
    public const string IndexStartParameter = "indexStart";

    private static readonly System.Collections.Generic.HashSet<string> ParameterWhiteList =
        [ChatIdParameter, IndexStartParameter];

    public static string GetCallback<TEnum>
        (string prefix, TEnum suffix, params ( string key, object value )[] parameters) where TEnum : Enum =>
        GetCallback(prefix, suffix.ToString(), parameters);

    public static string GetCallback(string prefix, string suffix, params ( string key, object value )[] parameters)
    {
        var sb = new StringBuilder(prefix).Append(PathSeparator).Append(suffix);

        if (parameters is { Length: > 0 })
        {
            foreach (var (key, value) in parameters)
            {
                sb.Append(PathSeparator).Append(key).Append(ParamSeparator).Append(value);
            }
        }

        return sb.ToString();
    }

    public static Option<(string prefix, string suffix, string[] parameters)> ParseChatCallback(string callback) =>
        callback.Split(PathSeparator) is [var prefix, var suffix, .. var parameters]
            ? (prefix, suffix, parameters)
            : None;

    public static Option<Map<string, string>> GetQueryParameters(string[] parts) => parts.Length > 0
        ? ParseQueryParameters(parts)
        : Option<Map<string, string>>.None;

    private static Option<Map<string, string>> ParseQueryParameters(string[] paramParts)
    {
        var dict = new Dictionary<string, string>();
        foreach (var part in paramParts)
        {
            var keyValue = part.Split(ParamSeparator, 2);
            if (!ParameterWhiteList.Contains(keyValue[0])) continue;
            if (keyValue.Length == 2) dict[keyValue[0]] = keyValue[1];
        }

        return dict.Count > 0 ? dict.ToMap() : Option<Map<string, string>>.None;
    }

    public bool HasChatIdKey(Map<string, string> parameters, out long chatId)
    {
        chatId = 0;
        return parameters.ContainsKey(ChatIdParameter) &&
               long.TryParse(parameters[ChatIdParameter], out chatId) &&
               messageAssistance.IsAllowedChat(chatId);
    }
}