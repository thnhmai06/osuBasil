using Basil.Application.Servers.Objects;
using Basil.Domain.Game.Users;

namespace Basil.Application.Servers.Capabilities;

public readonly struct ScopedServerFaq
{
	private readonly ScopedServer _scoped;

	internal ScopedServerFaq(ScopedServer scoped)
	{
		_scoped = scoped;
	}

	private ServerOutcome? Denied()
	{
		return _scoped.Lacks(UserPrivileges.Owner)
			? ServerOutcome.Denied
			: null;
	}

	public async Task<ServerOutcome> SaveAsync(string entry, string text)
	{
		if (Denied() is { } denied) return denied;

		return await _scoped.Server.Faq.SaveAsync(entry, text);
	}

	public async Task<ServerOutcome> DeleteAsync(string entry)
	{
		if (Denied() is { } denied) return denied;

		return await _scoped.Server.Faq.DeleteAsync(entry);
	}
}