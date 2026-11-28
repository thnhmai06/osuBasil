using Basil.Application.Shared.Storage;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Storage;
using Basil.Infrastructure.Shared.Storage;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class OpenUserAvatarHandler(IUserAvatarStorage avatars)
	: IRequestHandler<OpenUserAvatar, ImageFile?>
{
	public async ValueTask<ImageFile?> Handle(OpenUserAvatar request, CancellationToken ct)
	{
		return await ImageFiles.DescribeAsync(await avatars.OpenAsync(request.UserId, ct), ct);
	}
}
