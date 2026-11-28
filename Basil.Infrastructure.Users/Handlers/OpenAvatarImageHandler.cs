using Basil.Application.Shared.Storage;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Storage;
using Basil.Infrastructure.Shared.Storage;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class OpenAvatarImageHandler(IUserAvatarStorage avatars) : IRequestHandler<OpenAvatarImage, ImageFile>
{
	private static readonly Lazy<byte[]> DefaultAvatar = new(() =>
	{
		using var stream = typeof(OpenAvatarImageHandler).Assembly.GetManifestResourceStream("Avatars.default.png")!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	});

	private static readonly DateTimeOffset DefaultAvatarModified = File.GetLastWriteTimeUtc(
		typeof(OpenAvatarImageHandler).Assembly.Location);

	public async ValueTask<ImageFile> Handle(OpenAvatarImage request, CancellationToken ct)
	{
		return await ImageFiles.DescribeAsync(await avatars.OpenAsync(request.UserId, ct), ct) ??
		       new ImageFile(new MemoryStream(DefaultAvatar.Value, false), "image/png", DefaultAvatarModified);
	}
}
