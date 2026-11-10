using Basil.Application.Users.Messages;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Storage;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Opens the avatar of an account for <see cref="OpenUserAvatar" />.</summary>
internal sealed class OpenUserAvatarHandler(IUserRepository users, IUserAvatarStorage avatars)
	: IRequestHandler<OpenUserAvatar, Stream?>
{
	/// <inheritdoc />
	public async ValueTask<Stream?> Handle(OpenUserAvatar request, CancellationToken ct)
	{
		return await users.GetAsync(request.UserId, ct) is { } user ? await avatars.OpenAsync(user, ct) : null;
	}
}