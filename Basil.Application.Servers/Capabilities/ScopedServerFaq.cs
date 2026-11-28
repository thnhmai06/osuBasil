using Basil.Application.Servers.Objects;
using Basil.Domain.Users;

namespace Basil.Application.Servers.Capabilities;

public readonly struct ScopedServerFaq
{
	private readonly ScopedServer _scoped;

	internal ScopedServerFaq(ScopedServer scoped)
	{
		_scoped = scoped;
	}

	private ServerActionResult? Denied()
	{
		return _scoped.Lacks(UserPrivileges.Owner)
			? ServerActionResult.Denied
			: null;
	}

	public ServerActionResult MayChange()
	{
		return Denied() ?? ServerActionResult.Ok;
	}

	public async Task<ServerActionResult> SaveAsync(string entry, string text)
	{
		if (Denied() is { } denied) return denied;

		return await _scoped.Server.Faq.SaveAsync(entry, text);
	}

	public async Task<ServerActionResult> DeleteAsync(string entry)
	{
		if (Denied() is { } denied) return denied;

		return await _scoped.Server.Faq.DeleteAsync(entry);
	}
}