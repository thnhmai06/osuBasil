using Basil.Application.Shared.Paging;
using Basil.Application.Servers.Objects;
using Basil.Domain.Content;
using Basil.Domain.Users;

namespace Basil.Application.Servers.Capabilities;

public readonly struct ScopedServerScreenshots
{
	private readonly ScopedServer _scoped;

	internal ScopedServerScreenshots(ScopedServer scoped)
	{
		_scoped = scoped;
	}

	public async Task<(ContentReference? Screenshot, ServerActionResult Result)> UploadAsync(Stream content,
		CancellationToken ct = default)
	{
		if (_scoped.Lacks(UserPrivileges.Player))
			return (null, ServerActionResult.Denied);

		return await _scoped.Server.Screenshots.UploadAsync(content, ct);
	}

	public async Task<(ServerActionResult Result, Page<ContentReference> Screenshots)> ListAsync(
		PageRequest? page = null, CancellationToken ct = default)
	{
		if (_scoped.Lacks(UserPrivileges.Moderator))
			return (ServerActionResult.Denied, new Page<ContentReference>([], false));

		return (ServerActionResult.Ok, await _scoped.Server.Screenshots.ListAsync(page, ct));
	}

	public async Task<ServerActionResult> DeleteAsync(string name)
	{
		if (_scoped.Lacks(UserPrivileges.Moderator))
			return ServerActionResult.Denied;

		return await _scoped.Server.Screenshots.DeleteAsync(name);
	}
}