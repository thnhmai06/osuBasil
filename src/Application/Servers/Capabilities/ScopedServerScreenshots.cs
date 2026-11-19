using Basil.Application.Servers.Objects;
using Basil.Domain.Content;
using Basil.Domain.Game.Users;

namespace Basil.Application.Servers.Capabilities;

public readonly struct ScopedServerScreenshots
{
	private readonly ScopedServer _scoped;

	internal ScopedServerScreenshots(ScopedServer scoped)
	{
		_scoped = scoped;
	}

	public async Task<(ContentReference? Screenshot, ServerOutcome Result)> UploadAsync(Stream content,
		CancellationToken ct = default)
	{
		if (_scoped.Lacks(UserPrivileges.Player))
			return (null, ServerOutcome.Denied);

		return await _scoped.Server.Screenshots.UploadAsync(content, ct);
	}

	public async Task<(ServerOutcome Result, IReadOnlyList<ContentReference> Screenshots)> ListAsync(
		CancellationToken ct = default)
	{
		if (_scoped.Lacks(UserPrivileges.Moderator))
			return (ServerOutcome.Denied, []);

		return (ServerOutcome.Ok, await _scoped.Server.Screenshots.ListAsync(ct));
	}

	public async Task<ServerOutcome> DeleteAsync(ContentReference screenshot)
	{
		if (_scoped.Lacks(UserPrivileges.Moderator))
			return ServerOutcome.Denied;

		return await _scoped.Server.Screenshots.DeleteAsync(screenshot);
	}
}