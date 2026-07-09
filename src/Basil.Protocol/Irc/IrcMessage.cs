namespace Basil.Protocol.Irc;

/// <summary>
///     A single IRC protocol line per RFC 1459 §2.3.1: [":prefix"] command param* [":trailing"].
///     Pure wire-format representation — no server/session semantics.
/// </summary>
public sealed record IrcMessage(string? Prefix, string Command, IReadOnlyList<string> Params);
