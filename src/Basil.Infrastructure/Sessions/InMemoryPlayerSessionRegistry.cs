using System.Collections.Concurrent;
using Basil.Application.Sessions;
using Basil.Domain.Users;

namespace Basil.Infrastructure.Sessions;

/// <inheritdoc cref="IPlayerSessionRegistry" />
/// <remarks>
///     Backed by a <see cref="ConcurrentDictionary{TKey,TValue}" /> keyed by login token.
///     <see cref="GetByToken" /> is an O(1) dictionary lookup; <see cref="GetById" /> and
///     <see cref="GetByName" /> scan the dictionary values, and the name lookup compares each
///     session's <see cref="PlayerSession.SafeName" /> against the safe form of the requested name.
///     Every read returns a snapshot, so callers never iterate a live collection.
/// </remarks>
public sealed class InMemoryPlayerSessionRegistry : IPlayerSessionRegistry
{
	private readonly ConcurrentDictionary<string, PlayerSession> _byToken = new();

	/// <inheritdoc />
	/// <remarks>Registering a session whose token is already present replaces the previous session.</remarks>
	public void Add(PlayerSession session)
	{
		_byToken[session.Token] = session;
	}

	/// <inheritdoc />
	public void Remove(PlayerSession session)
	{
		_byToken.TryRemove(session.Token, out _);
	}

	/// <inheritdoc />
	public PlayerSession? GetByToken(string token)
	{
		return _byToken.GetValueOrDefault(token);
	}

	/// <inheritdoc />
	/// <remarks>Returns the first session whose <see cref="PlayerSession.Id" /> matches.</remarks>
	public PlayerSession? GetById(int id)
	{
		return _byToken.Values.FirstOrDefault(s => s.Id == id);
	}

	/// <inheritdoc />
	/// <remarks>
	///     Normalizes <paramref name="name" /> with <see cref="User.MakeSafeName" /> and compares it
	///     against each session's <see cref="PlayerSession.SafeName" />.
	/// </remarks>
	public PlayerSession? GetByName(string name)
	{
		var safeName = User.MakeSafeName(name);
		return _byToken.Values.FirstOrDefault(s => s.SafeName == safeName);
	}

	/// <inheritdoc />
	public IReadOnlyList<PlayerSession> All => [.. _byToken.Values];
}