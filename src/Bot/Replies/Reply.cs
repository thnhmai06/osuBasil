namespace Basil.Bot.Replies;

/// <summary>
///     A user-visible reply BasilBot can say: the key it has in the replies file, the text it has unless that file
///     changes it, and the arguments the text may use.
/// </summary>
/// <param name="Key">The key of the reply in the replies file.</param>
/// <param name="Default">The text of the reply in ICU message format, with each argument written as <c>{name}</c>.</param>
/// <param name="Arguments">The names of the arguments every text of the reply may use.</param>
internal sealed record Reply(string Key, string Default, params string[] Arguments);