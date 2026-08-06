using Basil.Application.Sessions;
using Basil.Domain.Users;

namespace Basil.Infrastructure.Sessions;

/// <inheritdoc cref="IUserSessionRegistry" />
/// <remarks>
///     Backed by a plain <see cref="Dictionary{TKey,TValue}" /> keyed by login token, plus two
///     UserId-keyed indices (one per session kind) so <see cref="GetGameByUserId" />/
///     <see cref="GetIrcByUserId" /> are O(1) and <see cref="TryAddGameSession" />/
///     <see cref="TryAddIrcSession" /> can check-then-insert atomically. All three collections are
///     mutated under one <see cref="_lock" />; every read returns a snapshot copy, so callers never
///     iterate a live collection.
/// </remarks>
public sealed class InMemoryUserSessionRegistry : IUserSessionRegistry
{
	private readonly object _lock = new();
	private readonly Dictionary<string, UserSession> _byToken = new();
	private readonly Dictionary<int, GameSession> _gamesByUserId = new();
	private readonly Dictionary<int, IrcSession> _ircsByUserId = new();

	/// <inheritdoc />
	public IReadOnlyCollection<UserSession> All
	{
		get { lock (_lock) { return _byToken.Values.ToArray(); } }
	}

	/// <inheritdoc />
	public IReadOnlyCollection<GameSession> GameSessions
	{
		get { lock (_lock) { return _gamesByUserId.Values.ToArray(); } }
	}

	/// <inheritdoc />
	public IReadOnlyCollection<IrcSession> IrcSessions
	{
		get { lock (_lock) { return _ircsByUserId.Values.ToArray(); } }
	}

	/// <inheritdoc />
	public bool TryAddGameSession(GameSession session)
	{
		lock (_lock)
		{
			if (_gamesByUserId.ContainsKey(session.Id)) return false;
			_gamesByUserId.Add(session.Id, session);
			_byToken.Add(session.Token, session);
			return true;
		}
	}

	/// <inheritdoc />
	public bool TryAddIrcSession(IrcSession session)
	{
		lock (_lock)
		{
			if (_ircsByUserId.ContainsKey(session.Id)) return false;
			_ircsByUserId.Add(session.Id, session);
			_byToken.Add(session.Token, session);
			return true;
		}
	}

	/// <inheritdoc />
	public void Remove(UserSession session)
	{
		lock (_lock)
		{
			if (!_byToken.TryGetValue(session.Token, out var current) || !ReferenceEquals(current, session)) return;

			_byToken.Remove(session.Token);
			switch (session)
			{
				case GameSession game when _gamesByUserId.TryGetValue(game.Id, out var g) && ReferenceEquals(g, game):
					_gamesByUserId.Remove(game.Id);
					break;
				case IrcSession irc when _ircsByUserId.TryGetValue(irc.Id, out var i) && ReferenceEquals(i, irc):
					_ircsByUserId.Remove(irc.Id);
					break;
			}
		}
	}

	/// <inheritdoc />
	public GameSession? GetGameByToken(string token)
	{
		lock (_lock)
		{
			return _byToken.TryGetValue(token, out var session) ? session as GameSession : null;
		}
	}

	/// <inheritdoc />
	public GameSession? GetGameByUserId(int userId)
	{
		lock (_lock)
		{
			return _gamesByUserId.GetValueOrDefault(userId);
		}
	}

	/// <inheritdoc />
	public GameSession? GetGameByName(string name)
	{
		var safeName = User.MakeSafeName(name);
		lock (_lock)
		{
			return _gamesByUserId.Values.FirstOrDefault(s => s.SafeName == safeName);
		}
	}

	/// <inheritdoc />
	public IrcSession? GetIrcByUserId(int userId)
	{
		lock (_lock)
		{
			return _ircsByUserId.GetValueOrDefault(userId);
		}
	}

	/// <inheritdoc />
	public IrcSession? GetIrcByName(string name)
	{
		var safeName = User.MakeSafeName(name);
		lock (_lock)
		{
			return _ircsByUserId.Values.FirstOrDefault(s => s.SafeName == safeName);
		}
	}

	/// <inheritdoc />
	public IReadOnlyList<UserSession> GetSessionsByUserId(int userId)
	{
		lock (_lock)
		{
			var result = new List<UserSession>(2);
			if (_gamesByUserId.TryGetValue(userId, out var game)) result.Add(game);
			if (_ircsByUserId.TryGetValue(userId, out var irc)) result.Add(irc);
			return result;
		}
	}
}
