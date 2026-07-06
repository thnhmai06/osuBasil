namespace Bancho.Protocol;

/// <summary>Ported from Message (NamedTuple) in app/packets.py.</summary>
public sealed record BanchoMessage(string Sender, string Text, string Recipient, int SenderId);
