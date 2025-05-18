using DecembristChatBotSharp.Entity;
using DecembristChatBotSharp.Telegram.MessageHandlers.ChatCommand.Items;

namespace DecembristChatBotSharp;

public record DustConfig(Dictionary<MemberItemType, DustRecipe> DustRecipes);