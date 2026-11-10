using Basil.Domain.Content;

namespace Basil.Application.Servers.Objects;

public sealed partial class Server
{
	/// <summary>Stores a screenshot under a new unique name after checking its size and picture.</summary>
	/// <param name="content">The image, read to its end; not disposed.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     Ok and the reference of the stored screenshot, or TooLarge, InvalidImage or DimensionsTooLarge and no
	///     reference.
	/// </returns>
	internal async Task<(ContentReference? Screenshot, ServerOutcome Outcome)> UploadScreenshotAsync(Stream content,
		CancellationToken cancellationToken)
	{
		var limits = (await Context.Settings.GetAsync(cancellationToken)).Uploads;

		await using var upload = await Context.Spool.SpoolAsync(content, limits.ScreenshotMaxSize, cancellationToken);
		if (upload.TooLarge) return (null, ServerOutcome.TooLarge);

		var info = await Context.Inspector.InspectAsync(upload.Content, cancellationToken);
		if (info is null || info.Width < 1 || info.Height < 1)
		{
			await upload.QuarantineAsync(cancellationToken);
			return (null, ServerOutcome.InvalidImage);
		}

		if (info.Width > limits.ScreenshotMaxSide || info.Height > limits.ScreenshotMaxSide)
		{
			await upload.QuarantineAsync(cancellationToken);
			return (null, ServerOutcome.DimensionsTooLarge);
		}

		upload.Content.Position = 0;
		var reference = await Context.Screenshots.SaveAsync(upload.Content, info.Extension);
		return (reference, ServerOutcome.Ok);
	}

	/// <summary>Lists the stored screenshots.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The references of the screenshots.</returns>
	internal async Task<IReadOnlyList<ContentReference>> ListScreenshotsAsync(CancellationToken cancellationToken)
	{
		return [.. await Context.Screenshots.ListAsync(cancellationToken)];
	}

	/// <summary>Deletes a stored screenshot.</summary>
	/// <param name="screenshot">The reference of the screenshot to delete.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, even when none is stored under the reference.</returns>
	internal async Task<ServerOutcome> DeleteScreenshotAsync(ContentReference screenshot,
		CancellationToken cancellationToken)
	{
		await Context.Screenshots.DeleteAsync(screenshot);
		return ServerOutcome.Ok;
	}
}