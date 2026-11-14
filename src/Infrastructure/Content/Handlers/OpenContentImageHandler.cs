using Basil.Application.Content.Messages;
using Basil.Application.Content.Storage;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Infrastructure.Content.Handlers;

/// <summary>Opens the picture <see cref="OpenContentImage" /> names.</summary>
internal sealed class OpenContentImageHandler(
	IMenuBannerStorage banners,
	IMenuSeasonalsStorage seasonals,
	IMenuIconStorage icon,
	IScreenshotStorage screenshots) : IRequestHandler<OpenContentImage, Stream?>
{
	/// <inheritdoc />
	public async ValueTask<Stream?> Handle(OpenContentImage request, CancellationToken ct)
	{
		var name = request.Name ?? string.Empty;
		return request.Kind switch
		{
			ContentImageKind.Icon => (await icon.OpenAsync(ct))?.Content,
			ContentImageKind.Banner => await OpenNamedAsync(await banners.ListAsync(ct), name, banners.OpenAsync, ct),
			ContentImageKind.Seasonal => await OpenNamedAsync(await seasonals.ListAsync(ct), name, seasonals.OpenAsync,
				ct),
			_ => await OpenNamedAsync(await screenshots.ListAsync(ct), name, screenshots.OpenAsync, ct)
		};
	}

	private static async Task<Stream?> OpenNamedAsync(IEnumerable<ContentReference> list, string name,
		Func<ContentReference, CancellationToken, Task<Stream?>> open, CancellationToken ct)
	{
		foreach (var reference in list)
			if (Path.GetFileName(reference.Value).Equals(name, StringComparison.OrdinalIgnoreCase))
				return await open(reference, ct);

		return null;
	}
}