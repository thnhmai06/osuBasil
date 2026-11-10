using Basil.Domain.Content;

namespace Basil.Application.Servers.Objects;

public sealed partial class Server
{
	/// <summary>Stores a screenshot under a new unique name after checking its size and picture.</summary>
	/// <param name="content">The image, read to its end; not disposed.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>
	///     Ok and the reference of the stored screenshot, or TooLarge, InvalidImage or DimensionsTooLarge and no
	///     reference.
	/// </returns>
	internal async Task<(ContentReference? Screenshot, ServerOutcome Outcome)> UploadScreenshotAsync(Stream content,
		CancellationToken ct)
	{
		var limits = (await Context.Settings.GetAsync(ct)).Uploads;

		await using var upload = await Context.Spool.SpoolAsync(content, limits.ScreenshotMaxSize, ct);
		if (upload.TooLarge) return (null, ServerOutcome.TooLarge);

		var info = await Context.Inspector.InspectAsync(upload.Content);
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
		var reference = await Context.Screenshots.SaveAsync(upload.Content, info.Extension);
		return (reference, ServerOutcome.Ok);
	}

	/// <summary>Lists the stored screenshots.</summary>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The references of the screenshots.</returns>
	internal async Task<IReadOnlyList<ContentReference>> ListScreenshotsAsync(CancellationToken ct)
	{
		return [.. await Context.Screenshots.ListAsync(ct)];
	}

	/// <summary>Deletes a stored screenshot.</summary>
	/// <param name="screenshot">The reference of the screenshot to delete.</param>
	/// <returns>Ok, even when none is stored under the reference.</returns>
	internal async Task<ServerOutcome> DeleteScreenshotAsync(ContentReference screenshot)
	{
		await Context.Screenshots.DeleteAsync(screenshot);
		return ServerOutcome.Ok;
	}
}