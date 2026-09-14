namespace Basil.Server.Features.Chat;

/// <summary>
///     One line of chat as the server understands it, before any transport has encoded it: who
///     said it, to which channel or player, and what.
/// </summary>
/// <param name="SenderId">The id of the player or bot the line is attributed to.</param>
/// <param name="SenderName">The display name of the sender.</param>
/// <param name="Target">The channel name or the recipient's name the line is addressed to.</param>
/// <param name="Text">The text of the line.</param>
/// <param name="Notice">
///     <see langword="true" /> when the line is a notice rather than ordinary chat; a transport that
///     has no notice concept delivers it as ordinary chat.
/// </param>
public sealed record ChatLine(int SenderId, string SenderName, string Target, string Text, bool Notice = false);
