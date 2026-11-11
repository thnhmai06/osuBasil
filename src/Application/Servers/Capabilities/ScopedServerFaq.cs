using Basil.Application.Common;
using Basil.Application.Servers.Objects;
using Basil.Domain.Content;
using Basil.Domain.Users;

namespace Basil.Application.Servers.Capabilities;

/// <summary>Changes the FAQ entries; every change requires <see cref="UserPrivileges.Owner" />.</summary>
public readonly struct ScopedServerFaq
{
	private readonly ScopedServer _scoped;

	/// <summary>Creates the scoped view of the FAQ entries.</summary>
	/// <param name="scoped">The scoped server.</param>
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

	/// <summary>Creates or replaces a FAQ entry.</summary>
	/// <param name="entry">The entry name; <c>:</c> separates folders.</param>
	/// <param name="text">The text of the entry.</param>
	/// <returns>The outcome.</returns>
	/// <exception cref="ArgumentException">The entry name is not a safe name.</exception>
	public async Task<ServerOutcome> SaveAsync(string entry, string text)
	{
		if (Denied() is { } denied) return denied;

		return await _scoped.Server.Faq.SaveAsync(entry, text);
	}

	/// <summary>Deletes a FAQ entry.</summary>
	/// <param name="entry">The entry name.</param>
	/// <returns>The outcome; deleting an entry that does not exist is ok.</returns>
	/// <exception cref="ArgumentException">The entry name is not a safe name.</exception>
	public async Task<ServerOutcome> DeleteAsync(string entry)
	{
		if (Denied() is { } denied) return denied;

		return await _scoped.Server.Faq.DeleteAsync(entry);
	}
}