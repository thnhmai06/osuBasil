namespace Basil.Protocol;

/// <summary>Represents a chat message as it is carried by the Bancho send-message packets.</summary>
/// <param name="Sender">The name of the sending player.</param>
/// <param name="Text">The message body.</param>
/// <param name="Recipient">The name of the receiving player or channel.</param>
/// <param name="SenderId">The id of the sending player.</param>
public sealed record BanchoMessage(string Sender, string Text, string Recipient, int SenderId);