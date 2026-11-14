using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks Users to open the avatar of an account.</summary>
/// <param name="UserId">The id of the account.</param>
/// <remarks>
///     Avatars are public; the answer is <see langword="null" /> when the account does not exist or has none, and the
///     receiver disposes the stream.
/// </remarks>
public sealed record OpenUserAvatar(int UserId) : IRequest<Stream?>;