namespace Basil.Protocol.Game.Stable.Models.Chat;

public sealed record StableMessage(string Sender, string Text, string Recipient, int SenderId);