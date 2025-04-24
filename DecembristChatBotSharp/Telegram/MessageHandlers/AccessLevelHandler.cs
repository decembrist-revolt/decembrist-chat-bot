using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Telegram.MessageHandlers;
using DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;
using Lamar;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class AccessLevelHandler(
    AdminUserRepository adminUserRepository,
    MemberItemRepository memberItemRepository)
{
    public async Task<CommandResult> Do(ICommandHandler handler, ChatMessageHandlerParams parameters)
    {
        var id = (parameters.TelegramId, parameters.ChatId);
        var accessResult = await CheckAccessLevel(handler, id);
        if (accessResult != CommandResult.Ok) return accessResult;

        await handler.Do(parameters);
        return CommandResult.Ok;
    }

    private async Task<CommandResult> CheckAccessLevel(ICommandHandler handler, CompositeId id) =>
        handler.CommandLevel switch
        {
            CommandLevel.User => CommandResult.Ok,
            CommandLevel.Admin => await CheckAdminLevel(id),
            CommandLevel.Item => await CheckItemLevel(handler.Command, id),
            _ => CommandResult.None
        };

    private async Task<CommandResult> CheckAdminLevel(CompositeId id) => await adminUserRepository.IsAdmin(id)
        ? CommandResult.Ok
        : CommandResult.NoAdmin;

    private async Task<CommandResult> CheckItemLevel(string command, CompositeId id)
    {
        if (!Enum.TryParse(command[1..], ignoreCase: true, out MemberItemType itemType))
        {
            return CommandResult.None;
        }

        var itemId = new MemberItem.CompositeId(id.TelegramId, id.ChatId, itemType);
        return await memberItemRepository.IsHaveItem(itemId) || await adminUserRepository.IsAdmin(id)
            ? CommandResult.Ok
            : CommandResult.NoItem;
    }
}