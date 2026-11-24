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
			if (stored.Length > limits.ScreenshotMaxSize) throw new UploadTooLargeException(limits.ScreenshotMaxSize);

			var info = await context.Inspector.InspectAsync(stored, token);
			if (info is null || info.Width < 1 || info.Height < 1) throw new InvalidImageException();
			if (info.Width > limits.ScreenshotMaxSide || info.Height > limits.ScreenshotMaxSide)
				throw new ImageDimensionsTooLargeException(limits.ScreenshotMaxSide);

			return info.Extension;
		}, ct);
		return (reference, ServerActionResult.Ok);
	}

	internal async Task<IReadOnlyList<ContentReference>> ListAsync(CancellationToken ct)
	{
		return [.. await _server.Context.Screenshots.ListAsync(ct)];
	}

	internal async Task<ServerActionResult> DeleteAsync(ContentReference screenshot)
	{
		if (!(await _server.Context.Screenshots.ListAsync()).Contains(screenshot)) return ServerActionResult.NotFound;

		await _server.Context.Screenshots.DeleteAsync(screenshot);
		return ServerActionResult.Ok;
	}
}