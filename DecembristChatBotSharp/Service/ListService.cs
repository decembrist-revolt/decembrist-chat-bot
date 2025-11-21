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
            ? x.Outputs[0].Item + QuantityString(x.Outputs[0])
            : string.Join(", ", x.Outputs.Select(o => $"{o.Item}{QuantityString(o)} - {o.Chance:P}"));
        return input + (" ⇒ " + output).EscapeMarkdown();
    }).ToImmutableList();

    private static string QuantityString(OutputItem output) =>
        output.Quantity > 1 ? $"({output.Quantity})" : string.Empty;

    public async Task<Option<(string, int)>> GetListBody(long chatId, ListType listType, int currentOffset = 0) =>
        listType switch
        {
            ListType.Lore => await FillListLore(currentOffset, chatId),
            ListType.FastReply => await FillListFastReply(currentOffset, chatId),
            ListType.Craft or ListType.Dust => FillListRecipes(listType, currentOffset),
            _ => None
        };

    private Task<Option<(string, int)>> FillListLore(int currentOffset, long chatId) =>
        loreRecordRepository.GetKeysCount(chatId).BindAsync(keysCount =>
        {
            if (keysCount < currentOffset) return None;

            var maybeResult = loreRecordRepository.GetLoreKeys(chatId, currentOffset);
            return maybeResult.BindAsync(keys =>
                {
                    var sb = new StringBuilder();
                    foreach (var key in keys)
                    {
                        sb.Append("• `").Append(key.EscapeMarkdown()).AppendLine("`");
                    }

                    return Some((sb.ToString(), keysCount));
                }
            );
        }).ToOption();

    private Task<Option<(string, int)>> FillListFastReply(int currentOffset, long chatId) =>
        fastReplyRepository.GetMessagesCount(chatId).BindAsync(keysCount =>
        {
            if (keysCount < currentOffset) return None;

            var maybeResult = fastReplyRepository.GetFastReplyMessages(chatId, currentOffset);
            return maybeResult.BindAsync(keysAndDate =>
                {
                    var sb = new StringBuilder();
                    foreach (var (key, date) in keysAndDate)
                    {
                        sb.Append("• `")
                            .Append(key.EscapeMarkdown())
                            .Append('`')
                            .AppendLine($" - {date:d}".EscapeMarkdown());
                    }

                    return Some((sb.ToString(), keysCount));
                }
            );
        }).ToOption();

    private Option<(string, int)> FillListRecipes(ListType listType, int currentOffset)
    {
        var maybeResult = listType switch
        {
            ListType.Dust => _dustRecipes,
            ListType.Craft => _craftRecipes,
            _ => []
        };
        if (maybeResult.IsEmpty || maybeResult.Count < currentOffset) return None;

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