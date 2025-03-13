namespace DecembristChatBotSharp;

public static class UtilsExtensions
{
    public static Task<Unit> UnitTask(this Task task) => task.ContinueWith(_ => unit);

    public static Unit Ignore(this object any) => unit;
}