using Basil.Application.Shared.Diagnostics;
using Basil.Application.Shared.Paging;
using Basil.Domain.Content;

namespace Basil.Application.Servers.Objects;

public sealed partial class Server
{
	public ServerScreenshots Screenshots => new(this);
}

public readonly struct ServerScreenshots
{
	private readonly Server _server;

	internal ServerScreenshots(Server server)
	{
		_server = server;
	}

	internal async Task<(ContentReference? Screenshot, ServerActionResult Outcome)> UploadAsync(Stream content,
		CancellationToken ct)
	{
		var context = _server.Context;
		var limits = (await context.Settings.GetAsync(ct)).Uploads;

		var reference = await context.Screenshots.SaveAsync(content, async (stored, token) =>
		{
			if (stored.Length > limits.ScreenshotMaxSize) throw UploadMetrics.Refuse(new UploadTooLargeException(limits.ScreenshotMaxSize));

			var info = await context.Inspector.InspectAsync(stored, token);
			if (info is null || info.Width < 1 || info.Height < 1) throw UploadMetrics.Refuse(new InvalidImageException());
			if (info.Width > limits.ScreenshotMaxSide || info.Height > limits.ScreenshotMaxSide)
				throw UploadMetrics.Refuse(new ImageDimensionsTooLargeException(limits.ScreenshotMaxSide));

			return info.Extension;
		}, ct);
		return (reference, ServerActionResult.Ok);
	}

	internal async Task<Page<ContentReference>> ListAsync(PageRequest? page, CancellationToken ct)
	{
		var all = (await _server.Context.Screenshots.ListAsync(ct)).ToList();
		if (page is null) return new Page<ContentReference>(all, false);

		return new Page<ContentReference>([.. all.Skip(page.Offset).Take(page.Limit)], page.Offset + page.Limit < all.Count);
	}

	internal async Task<ServerActionResult> DeleteAsync(string name)
	{
		var screenshot = (await _server.Context.Screenshots.ListAsync()).Cast<ContentReference?>()
			.FirstOrDefault(r => r!.Value.FileName == name);
		if (screenshot is null) return ServerActionResult.NotFound;

		await _server.Context.Screenshots.DeleteAsync(screenshot.Value);
		return ServerActionResult.Ok;
	}
}