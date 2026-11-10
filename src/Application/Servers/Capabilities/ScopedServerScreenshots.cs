using Basil.Application.Common;
using Basil.Application.Servers.Objects;
using Basil.Domain.Content;
using Basil.Domain.Users;

namespace Basil.Application.Servers.Capabilities;

/// <summary>Screenshot operations scoped to one connection.</summary>
public readonly struct ScopedServerScreenshots
{
	private readonly ScopedServer _scoped;

	/// <summary>Creates screenshot operations for a server scope.</summary>
	/// <param name="scoped">The server scope.</param>
	internal ScopedServerScreenshots(ScopedServer scoped)
	{
		_scoped = scoped;
	}

	/// <summary>Stores a screenshot under a new unique name.</summary>
	/// <param name="content">The image, read to its end; not disposed.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The reference to the stored screenshot, or why it was not stored.</returns>
	/// <remarks>
	///     Requires <see cref="UserPrivileges.Player" />. The image may be at most the screenshot size
	///     limit of the server settings in bytes and must be a PNG or JPEG picture of at most the screenshot side
	///     limit in pixels per side, told from its content, never from a name.
	/// </remarks>
	public async Task<(ContentReference? Screenshot, Result<ServerOutcome> Result)> UploadAsync(Stream content,
		CancellationToken ct = default)
	{
		if (_scoped.Needs(UserPrivileges.Player) is { } missing)
			return (null, Result<ServerOutcome>.Denied(missing));

		return await _scoped.Server.UploadScreenshotAsync(content, ct);
	}

	/// <summary>Lists the stored screenshots.</summary>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The outcome and, when successful, the references of the screenshots.</returns>
	/// <remarks>Requires <see cref="UserPrivileges.Moderator" />.</remarks>
	public async Task<(Result<ServerOutcome> Result, IReadOnlyList<ContentReference> Screenshots)> ListAsync(
		CancellationToken ct = default)
	{
		if (_scoped.Needs(UserPrivileges.Moderator) is { } missing)
			return (Result<ServerOutcome>.Denied(missing), []);

		return (ServerOutcome.Ok, await _scoped.Server.ListScreenshotsAsync(ct));
	}

	/// <summary>Deletes a stored screenshot.</summary>
	/// <param name="screenshot">The reference of the screenshot to delete.</param>
	/// <returns>Ok when the caller may delete screenshots, even when none is stored under the reference.</returns>
	/// <remarks>Requires <see cref="UserPrivileges.Moderator" />.</remarks>
	public async Task<Result<ServerOutcome>> DeleteAsync(ContentReference screenshot)
	{
		if (_scoped.Needs(UserPrivileges.Moderator) is { } missing)
			return Result<ServerOutcome>.Denied(missing);

		return await _scoped.Server.DeleteScreenshotAsync(screenshot);
	}
}