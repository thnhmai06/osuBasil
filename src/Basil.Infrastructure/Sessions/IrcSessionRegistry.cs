using System.Collections.Concurrent;
using Basil.Application.Sessions;
using Basil.Domain.Users;

namespace Basil.Infrastructure.Sessions;

/// <inheritdoc cref="ISessionRegistry{TSession}" />
/// <remarks>
///     Stores sessions keyed by login token, with UserId→token and SafeName→token indices so
///     lookups by user id or name resolve without iterating. All operations are thread-safe; every
///     read returns a snapshot rather than a live collection.
/// </remarks>
public sealed class IrcSessionRegistry : ISessionRegistry<IrcSession>
{
	private readonly ConcurrentDictionary<string, string> _bySafeName = new();
	private readonly ConcurrentDictionary<string, IrcSession> _byToken = new();
	private readonly ConcurrentDictionary<int, string> _byUserId = new();

	/// <inheritdoc />
	public IReadOnlyCollection<IrcSession> All => (IReadOnlyCollection<IrcSession>)_byToken.Values;

	/// <inheritdoc />
	public bool TryAdd(IrcSession session)
	{
		if (!_byUserId.TryAdd(session.Id, session.Token)) return false;
		_bySafeName[session.SafeName] = session.Token;
		_byToken[session.Token] = session;
		return true;
	}

	/// <inheritdoc />
	public void Remove(IrcSession session)
	{
		if (!_byUserId.TryGetValue(session.Id, out var token)
		    || !ReferenceEquals(_byToken.GetValueOrDefault(token), session)) return;

		_byUserId.TryRemove(session.Id, out _);
		_bySafeName.TryRemove(session.SafeName, out _);
		_byToken.TryRemove(token, out _);
	}

	/// <inheritdoc />
	public IrcSession? GetByToken(string token)
	{
		return _byToken.GetValueOrDefault(token);
	}

	/// <inheritdoc />
	public IrcSession? GetByUserId(int userId)
	{
		return _byUserId.TryGetValue(userId, out var token) ? _byToken.GetValueOrDefault(token) : null;
	}

	/// <inheritdoc />
	public IrcSession? GetByName(string name)
	{
		return _bySafeName.TryGetValue(User.MakeSafeName(name), out var token)
			? _byToken.GetValueOrDefault(token)
			: null;
	}
}