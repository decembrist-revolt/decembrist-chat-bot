using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Telegram.MessageHandlers;
using DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;
using Lamar;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class TipsRegistrationService(
    BotClient botClient,
    AdminUserRepository adminUserRepository,
    Lazy<IReadOnlyList<ICommandHandler>> commandHandlers,
    CancellationTokenSource cancelToken)
{
    private readonly BotCommand[] _userCommands =
        commandHandlers.Value.GetCommandsByLevel(CommandLevel.All & ~CommandLevel.Admin);

    private readonly BotCommand[] _adminCommands = commandHandlers.Value.GetCommandsByLevel(CommandLevel.Admin);
    private BotCommand[]? _allCommands;
    private BotCommand[] GetAllCommands() => _allCommands ??= _userCommands.Concat(_adminCommands).ToArray();

    public async Task RegisterTipsCommand()
    {
        await SetCommandsForScope(_userCommands, scope: new BotCommandScopeAllGroupChats());
        await SetCommandsForScope([], scope: new BotCommandScopeAllPrivateChats());
        await RegisterAdminCommand();
    }

    private async Task RegisterAdminCommand()
    {
        var admins = await adminUserRepository.GetAdmins();
        if (admins.Count == 0) return;

        var adminsScope = admins
            .Select(x => new BotCommandScopeChatMember { ChatId = x.Id.ChatId, UserId = x.Id.TelegramId });
        var allCommands = GetAllCommands();
        foreach (var admin in adminsScope)
        {
            await SetCommandsForScope(allCommands, scope: admin);
        }
    }

    private async Task SetCommandsForScope(BotCommand[] commands, BotCommandScope scope) =>
        await botClient.SetMyCommands(commands, scope: scope, cancellationToken: cancelToken.Token);
}