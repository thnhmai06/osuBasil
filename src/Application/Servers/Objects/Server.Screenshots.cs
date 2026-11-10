using Basil.Domain.Content;

namespace Basil.Application.Servers.Objects;

public sealed partial class Server
{
	/// <summary>Gets the screenshots players uploaded, used without a rights check.</summary>
	public ServerScreenshots Screenshots => new(this);
}

/// <summary>The screenshots players uploaded, used without a rights check.</summary>
public readonly struct ServerScreenshots
{
	private readonly Server _server;

	/// <summary>Creates the view of a server.</summary>
	/// <param name="server">The server.</param>
	internal ServerScreenshots(Server server)
	{
		_server = server;
	}

	/// <summary>Stores a screenshot under a new unique name after checking its size and picture.</summary>
	/// <param name="content">The image, read to its end; not disposed.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>
	///     Ok and the reference of the stored screenshot, or TooLarge, InvalidImage or DimensionsTooLarge and no
	///     reference.
	/// </returns>
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

	/// <summary>Lists the stored screenshots.</summary>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The references of the screenshots.</returns>
	internal async Task<IReadOnlyList<ContentReference>> ListAsync(CancellationToken ct)
	{
		return [.. await _server.Context.Screenshots.ListAsync(ct)];
	}

	/// <summary>Deletes a stored screenshot.</summary>
	/// <param name="screenshot">The reference of the screenshot to delete.</param>
	/// <returns>Ok, even when none is stored under the reference.</returns>
	internal async Task<ServerOutcome> DeleteAsync(ContentReference screenshot)
	{
		await _server.Context.Screenshots.DeleteAsync(screenshot);
		return ServerOutcome.Ok;
	}
}