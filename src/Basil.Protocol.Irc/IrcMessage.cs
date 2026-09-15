namespace Basil.Protocol.Irc;

/// <summary>
///     A single IRC protocol line per RFC 1459 §2.3.1: [":prefix"] command param* [":trailing"].
///     A pure wire-format representation with no server or session semantics.
/// </summary>
/// <param name="Prefix">
///     The optional source prefix, either a server name or a user hostmask; <see langword="null" /> when
///     absent.
/// </param>
/// <param name="Command">The IRC command name or three-digit numeric reply code.</param>
/// <param name="Params">The middle parameters of the line, plus the trailing parameter when present.</param>
public sealed record IrcMessage(string? Prefix, string Command, IReadOnlyList<string> Params);