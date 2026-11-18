using Basil.Application.Users.Messages;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Storage;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class OpenUserAvatarHandler(IUserRepository users, IUserAvatarStorage avatars)
	: IRequestHandler<OpenUserAvatar, Stream?>
{
	public async ValueTask<Stream?> Handle(OpenUserAvatar request, CancellationToken ct)
	{
		return await users.GetAsync(request.UserId, ct) is { } user ? await avatars.OpenAsync(user, ct) : null;
	}
}