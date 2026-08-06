using System.Collections.Concurrent;
using Basil.Application.Sessions;
using Basil.Domain.Users;

namespace Basil.Infrastructure.Sessions;

/// <inheritdoc cref="ISessionRegistry{TSession}" />
/// <remarks>
///     Backed by a <see cref="ConcurrentDictionary{TKey,TValue}" /> keyed by login token (the
///     authoritative store) plus two key-only indices — UserId→token and SafeName→token — so
///     <see cref="ISessionRegistry{TSession}.GetByUserId" />/<see cref="ISessionRegistry{TSession}.GetByName" />
///     resolve through the token without duplicating the stored session, and
///     <see cref="ISessionRegistry{TSession}.TryAdd" /> can check-then-insert atomically. No lock is
///     needed: <see cref="ConcurrentDictionary{TKey,TValue}.TryAdd" /> on the UserId index is the
///     atomic gate that decides the single winner between racing logins, and every read returns a
///     snapshot, so callers never iterate a live collection.
/// </remarks>
public sealed class GameSessionRegistry : ISessionRegistry<GameSession>
{
	private readonly ConcurrentDictionary<string, GameSession> _byToken = new();
	private readonly ConcurrentDictionary<int, string> _byUserId = new();
	private readonly ConcurrentDictionary<string, string> _bySafeName = new();

	/// <inheritdoc />
	public IReadOnlyCollection<GameSession> All => (IReadOnlyCollection<GameSession>)_byToken.Values;

	/// <inheritdoc />
	public bool TryAdd(GameSession session)
	{
		if (!_byUserId.TryAdd(session.Id, session.Token)) return false;
		_bySafeName[session.SafeName] = session.Token;
		_byToken[session.Token] = session;
		return true;
	}

	/// <inheritdoc />
	public void Remove(GameSession session)
	{
		if (!_byUserId.TryGetValue(session.Id, out var token)
		    || !ReferenceEquals(_byToken.GetValueOrDefault(token), session)) return;

		_byUserId.TryRemove(session.Id, out _);
		_bySafeName.TryRemove(session.SafeName, out _);
		_byToken.TryRemove(token, out _);
	}

	/// <inheritdoc />
	public GameSession? GetByToken(string token)
	{
		return _byToken.GetValueOrDefault(token);
	}

	/// <inheritdoc />
	public GameSession? GetByUserId(int userId)
	{
		return _byUserId.TryGetValue(userId, out var token) ? _byToken.GetValueOrDefault(token) : null;
	}

	/// <inheritdoc />
	public GameSession? GetByName(string name)
	{
		return _bySafeName.TryGetValue(User.MakeSafeName(name), out var token)
			? _byToken.GetValueOrDefault(token)
			: null;
	}
}