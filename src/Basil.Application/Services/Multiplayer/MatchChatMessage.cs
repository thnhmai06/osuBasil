namespace Basil.Application.Services.Multiplayer;

/// <summary>
///     One line of chat said in a match's own channel.
/// </summary>
/// <remarks>
///     Every line reaching the room is carried, whoever said it and however it arrived — an osu!
///     client, an IRC connection, or BasilBot answering a command. Chat is not stored, so this shape
///     only ever appears on the live stream, never as a history a caller can read back.
/// </remarks>
/// <param name="Sender">The user who said the line.</param>
/// <param name="Text">The line as it was said.</param>
/// <param name="SentAt">The moment the line reached the room.</param>
public sealed record MatchChatMessage(UserBrief Sender, string Text, DateTimeOffset SentAt);