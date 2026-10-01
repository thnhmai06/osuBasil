using Basil.Domain.Users;

namespace Basil.Domain.Chat;

/// <summary>A message posted to a chat channel.</summary>
/// <param name="Author">The user who posted the message.</param>
/// <param name="Content">The text of the message.</param>
/// <param name="Timestamp">The date and time the message was posted.</param>
public sealed record Message(User Author, string Content, DateTimeOffset Timestamp);