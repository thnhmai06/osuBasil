using Basil.Application.Shared.Storage;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks for the picture to show for a user's avatar: the user's own avatar, or the default avatar when the user has none.</summary>
/// <param name="UserId">The id of the user.</param>
public sealed record OpenAvatarImage(int UserId) : IRequest<ImageFile>;
