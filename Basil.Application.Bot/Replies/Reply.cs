namespace Basil.Application.Bot.Replies;

internal sealed record Reply(string Key, string Default, params string[] Arguments);