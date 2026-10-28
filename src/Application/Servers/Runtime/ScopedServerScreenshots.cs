using Basil.Application.Common;
using Basil.Domain.Content;
using Basil.Domain.Users;

namespace Basil.Application.Servers.Runtime;

/// <summary>Screenshot operations scoped to one connection.</summary>
public sealed class ScopedServerScreenshots
{
	/// <summary>The largest screenshot accepted, in bytes: 4 MiB.</summary>
	public const int MaxScreenshotBytes = 4 * 1024 * 1024;

	/// <summary>The largest accepted screenshot width, in pixels.</summary>
	public const int MaxScreenshotWidth = 8192;

	/// <summary>The largest accepted screenshot height, in pixels.</summary>
	public const int MaxScreenshotHeight = 8192;

	private readonly ScopedServer _scoped;

	/// <summary>Creates screenshot operations for a server scope.</summary>
	/// <param name="scoped">The server scope.</param>
	internal ScopedServerScreenshots(ScopedServer scoped)
	{
		_scoped = scoped;
	}

	/// <summary>Stores a screenshot under a new unique name.</summary>
	/// <param name="content">The image, read to its end; not disposed.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The reference to the stored screenshot, or why it was not stored.</returns>
	/// <remarks>
	///     Requires <see cref="UserPermissions.ScreenshotUpload" />. The image may be at most
	///     <see cref="MaxScreenshotBytes" /> bytes and must be a PNG or JPEG picture of at most
	///     <see cref="MaxScreenshotWidth" /> by <see cref="MaxScreenshotHeight" /> pixels per side, told from its
	///     content, never from a name.
	/// </remarks>
	public async Task<(ContentReference? Screenshot, Result<ServerOutcome> Result)> UploadAsync(Stream content,
		CancellationToken cancellationToken = default)
	{
		if (_scoped.Needs(UserPermissions.ScreenshotUpload) is { } missing)
			return (null, Result<ServerOutcome>.Denied(missing));

		var context = _scoped.Server.Context;

		using var memory = new MemoryStream();
		var buffer = new byte[81920];
		while (true)
		{
			var allowance = (int)Math.Min(buffer.Length, MaxScreenshotBytes + 1 - memory.Length);
			if (allowance == 0) break;
			var read = await content.ReadAsync(buffer.AsMemory(0, allowance), cancellationToken);
			if (read == 0) break;
			memory.Write(buffer, 0, read);
		}

		if (memory.Length > MaxScreenshotBytes) return (null, ServerOutcome.TooLarge);

		memory.Position = 0;
		var info = await context.Inspector.InspectAsync(memory, cancellationToken);
		if (info is null || info.Width < 1 || info.Height < 1)
			return (null, ServerOutcome.InvalidImage);
		if (info.Width > MaxScreenshotWidth || info.Height > MaxScreenshotHeight)
			return (null, ServerOutcome.DimensionsTooLarge);

		memory.Position = 0;
		var reference = await context.Screenshots.SaveAsync(memory, info.Extension, cancellationToken);
		return (reference, ServerOutcome.Ok);
	}

	/// <summary>Lists the stored screenshots.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome and, when successful, the references of the screenshots.</returns>
	/// <remarks>Requires <see cref="UserPermissions.ScreenshotManage" />.</remarks>
	public async Task<(Result<ServerOutcome> Result, IReadOnlyList<ContentReference> Screenshots)> ListAsync(
		CancellationToken cancellationToken = default)
	{
		if (_scoped.Needs(UserPermissions.ScreenshotManage) is { } missing)
			return (Result<ServerOutcome>.Denied(missing), []);

		return (ServerOutcome.Ok,
			[.. await _scoped.Server.Context.Screenshots.ListAsync(cancellationToken)]);
	}

	/// <summary>Deletes a stored screenshot.</summary>
	/// <param name="screenshot">The reference of the screenshot to delete.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok when the caller may delete screenshots, even when none is stored under the reference.</returns>
	/// <remarks>Requires <see cref="UserPermissions.ScreenshotManage" />.</remarks>
	public async Task<Result<ServerOutcome>> DeleteAsync(ContentReference screenshot,
		CancellationToken cancellationToken = default)
	{
		if (_scoped.Needs(UserPermissions.ScreenshotManage) is { } missing)
			return Result<ServerOutcome>.Denied(missing);

		await _scoped.Server.Context.Screenshots.DeleteAsync(screenshot, cancellationToken);
		return ServerOutcome.Ok;
	}
}