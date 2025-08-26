using System.Collections.Immutable;
using System.Text;
using DecembristChatBotSharp.Mongo;
using DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand;
using Lamar;

namespace DecembristChatBotSharp.Service;

[Singleton]
public class ListService(
    AppConfig appConfig,
    LoreRecordRepository loreRecordRepository,
    FastReplyRepository fastReplyRepository)
{
    private readonly ImmutableList<string> _dustRecipes = appConfig.DustConfig.DustRecipes.Select(dustRecipe =>
            $"• `{dustRecipe.Key.ToString().EscapeMarkdown()}`{$" ⇒ {dustRecipe.Value.Reward.Item} - {dustRecipe.Value.Reward.Range.Min}-{dustRecipe.Value.Reward.Range.Max}".EscapeMarkdown()}")
        .ToImmutableList();

    private readonly ImmutableList<string> _craftRecipes = appConfig.CraftConfig.Recipes.Select(x =>
    {
        var input = $"• `{string.Join(" ", x.Inputs.Select(iq => $"{iq.Item}@{iq.Quantity}")).EscapeMarkdown()}`";
        var output = x.Outputs.Count == 1
            ? x.Outputs[0].Item.ToString()
            : string.Join(", ", x.Outputs.Select(i => $"{i.Item} - {i.Chance:P}"));
        return input + ("⇒" + output).EscapeMarkdown();
    }).ToImmutableList();

    public async Task<Option<(string, int)>> GetKeys(long chatId, ListType listType, int currentOffset = 0)
    {
        if (listType is ListType.Craft or ListType.Dust)
        {
            return FillListRecipes(listType, currentOffset);
        }

        var maybeCount = listType switch
        {
            ListType.FastReply => await fastReplyRepository.GetMessagesCount(chatId),
            ListType.Lore => await loreRecordRepository.GetKeysCount(chatId),
            _ => None
        };
        return await maybeCount.MatchAsync(
            None: () => None,
            Some: async keysCount =>
            {
                if (keysCount < currentOffset) return None;
                var h = await FillList(listType, currentOffset, chatId);
                return h.Match(
                    x => Some((x, m: keysCount)),
                    () => None);
            });
    }

    private async Task<Option<string>> FillList(ListType listType, int currentOffset, long chatId)
    {
        var maybeResult = listType switch
        {
            ListType.FastReply => await fastReplyRepository.GetFastReplyMessages(chatId, currentOffset),
            ListType.Lore => await loreRecordRepository.GetLoreKeys(chatId, currentOffset),
            _ => None
        };
        return maybeResult.Match(
            None: () => None,
            Some: keys =>
            {
                var sb = new StringBuilder();
                foreach (var key in keys)
                {
                    var escape = key.EscapeMarkdown();
                    sb.Append("• `").Append(escape).AppendLine("`");
                }

                return Some(sb.ToString());
            }
        );
    }

    private Option<(string, int)> FillListRecipes(ListType listType, int currentOffset)
    {
        var maybeResult = listType switch
        {
            ListType.Dust => _dustRecipes,
            ListType.Craft => _craftRecipes,
            _ => []
        };
        if (maybeResult.IsEmpty) return None;

        var sb = new StringBuilder();
        foreach (var line in maybeResult.Skip(currentOffset).Take(appConfig.ListConfig.RowLimit))
        {
            sb.AppendLine(line);
        }

        return (sb.ToString(), maybeResult.Count);
    }

    public bool IsContainIndex(Map<string, string> parameters, out int currentOffset)
    {
        currentOffset = 0;
        return parameters.ContainsKey(CallbackService.IndexStartParameter) &&
               int.TryParse(parameters[CallbackService.IndexStartParameter], out currentOffset);
    }
}