namespace Basil.Protocol.Stable.Models.Chat;

public sealed record StableMessage(string Sender, string Text, string Recipient, int SenderId);