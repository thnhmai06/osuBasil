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

	internal async Task<(ContentReference? Screenshot, ServerOutcome Outcome)> UploadAsync(Stream content,
		CancellationToken ct)
	{
		var limits = (await _server.Context.Settings.GetAsync(ct)).Uploads;

		await using var upload = await _server.Context.Spool.SpoolAsync(content, limits.ScreenshotMaxSize, ct);
		if (upload.TooLarge) return (null, ServerOutcome.TooLarge);

		var info = await _server.Context.Inspector.InspectAsync(upload.Content);
		if (info is null || info.Width < 1 || info.Height < 1)
		{
			await upload.QuarantineAsync();
			return (null, ServerOutcome.InvalidImage);
		}

		if (info.Width > limits.ScreenshotMaxSide || info.Height > limits.ScreenshotMaxSide)
		{
			await upload.QuarantineAsync();
			return (null, ServerOutcome.DimensionsTooLarge);
		}

		upload.Content.Position = 0;
		var reference = await _server.Context.Screenshots.SaveAsync(upload.Content, info.Extension);
		return (reference, ServerOutcome.Ok);
	}

	internal async Task<IReadOnlyList<ContentReference>> ListAsync(CancellationToken ct)
	{
		return [.. await _server.Context.Screenshots.ListAsync(ct)];
	}

	internal async Task<ServerOutcome> DeleteAsync(ContentReference screenshot)
	{
		if (!(await _server.Context.Screenshots.ListAsync()).Contains(screenshot)) return ServerOutcome.NotFound;

		await _server.Context.Screenshots.DeleteAsync(screenshot);
		return ServerOutcome.Ok;
	}
}