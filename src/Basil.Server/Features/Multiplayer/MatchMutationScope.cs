namespace Basil.Server.Features.Multiplayer;

/// <summary>
///     Performs the actual snapshot builds and broadcasts a <see cref="MatchMutationScope" /> requests
///     during disposal, once the match's lock has already been released.
/// </summary>
/// <remarks>
///     The one production implementation is <see cref="MatchBroadcast" />, which already owns
///     every repository and registry a snapshot build touches. This interface exists so
///     <see cref="MatchMutationScope" />'s own tests can exercise the scope's lock, version, and
///     exception semantics against a lightweight fake instead of that whole dependency graph.
/// </remarks>
public interface IMatchMutationPublisher
{
	/// <summary>Broadcasts the match state to the channel and lobby and republishes the main, settings, and slot channels.</summary>
	Task PublishStateAsync(MatchSession match, long version, bool lobby, CancellationToken cancellationToken);

	/// <summary>Rebuilds and republishes the host snapshot channel.</summary>
	Task PublishHostAsync(MatchSession match, long version, CancellationToken cancellationToken);

	/// <summary>Rebuilds and republishes the referee list snapshot channel.</summary>
	Task PublishRefsAsync(MatchSession match, long version, CancellationToken cancellationToken);

	/// <summary>Rebuilds and republishes the banlist snapshot channel.</summary>
	Task PublishBansAsync(MatchSession match, long version, CancellationToken cancellationToken);

	/// <summary>Republishes the countdown timer snapshot channel.</summary>
	void PublishTimer(MatchSession match, long version);
}

/// <summary>
///     One read-mutate-broadcast sequence on a match, holding the match's lock for the mutation and
///     owning the state version the broadcast carries.
/// </summary>
/// <remarks>
///     Callers used to allocate a version by hand inside the lock and thread it through every
///     publish call, which meant every call site could forget to increment, could pass a stale
///     value, or could hold the lock across the broadcast. This scope owns that lifecycle instead.
///
///     Disposal does exactly three things, in this order: allocate the version if any publish was
///     requested, release the lock, then run the requested publishes unlocked. Building and
///     broadcasting outside the lock is what keeps a slow build from serializing the match, and the
///     version allocated inside it is what lets a build that finishes out of order be dropped
///     rather than reverting live state.
///
///     A scope that requests no publish allocates no version. Repeated publish requests for the
///     same stream coalesce into one publish at one version. An exception inside the scope still
///     publishes what was requested before it: the state change that happened is real, and hiding
///     it would leave every subscriber silently stale. A publish that fails is caught and never
///     rethrown -- the mutation has already committed to memory and the live stream is a
///     projection of it, not a participant in it.
///
///     The match lock is not reentrant, so a nested scope on the same match would deadlock. It
///     throws instead.
/// </remarks>
public sealed class MatchMutationScope : IAsyncDisposable
{
	private readonly IMatchMutationPublisher? _publisher;
	private readonly CancellationToken _cancellationToken;
	private bool _completed;
	private bool _publishState;
	private bool _publishStateLobby;
	private bool _publishHost;
	private bool _publishRefs;
	private bool _publishBans;
	private bool _publishTimer;

	internal MatchMutationScope(MatchSession session, IMatchMutationPublisher? publisher,
		CancellationToken cancellationToken)
	{
		Session = session;
		_publisher = publisher;
		_cancellationToken = cancellationToken;
	}

	/// <summary>
	///     Gets the match this scope holds the lock for. Nothing prevents a caller from capturing this
	///     past the scope's disposal; do not use it after the <c>await using</c> block ends.
	/// </summary>
	public MatchSession Session { get; }

	/// <summary>
	///     Gets the state version this mutation allocated, or <see langword="null" /> if no publish was
	///     requested and disposal has not run yet, or none ever was.
	/// </summary>
	public long? AllocatedVersion { get; private set; }

	/// <summary>Requests that the match state (settings, slots, host) be rebuilt and broadcast when this scope completes.</summary>
	/// <param name="lobby"><see langword="true" /> to also broadcast to the lobby; otherwise, <see langword="false" />.</param>
	public void PublishState(bool lobby = true)
	{
		_publishState = true;
		_publishStateLobby = lobby;
	}

	/// <summary>Requests that the host snapshot channel be rebuilt and republished when this scope completes.</summary>
	public void PublishHost() => _publishHost = true;

	/// <summary>Requests that the referee list snapshot channel be rebuilt and republished when this scope completes.</summary>
	public void PublishRefs() => _publishRefs = true;

	/// <summary>Requests that the banlist snapshot channel be rebuilt and republished when this scope completes.</summary>
	public void PublishBans() => _publishBans = true;

	/// <summary>Requests that the countdown timer snapshot channel be republished when this scope completes.</summary>
	public void PublishTimer() => _publishTimer = true;

	/// <summary>
	///     Allocates the version if any publish was requested, releases the match's lock, then runs
	///     the requested publishes unlocked. Safe to call more than once; only the first call does
	///     anything. Called automatically by <see cref="DisposeAsync" />; call it directly when the
	///     completion needs to appear at a specific point in the code.
	/// </summary>
	public async ValueTask CompleteAsync()
	{
		if (_completed) return;
		_completed = true;

		var anyPublishRequested = _publishState || _publishHost || _publishRefs || _publishBans || _publishTimer;
		var version = anyPublishRequested ? Session.AllocateStateVersion() : (long?)null;
		AllocatedVersion = version;

		Session.ReleaseMutation();

		if (version is not { } v || _publisher is null) return;

		if (_publishState) await RunAsync(() => _publisher.PublishStateAsync(Session, v, _publishStateLobby, _cancellationToken));
		if (_publishHost) await RunAsync(() => _publisher.PublishHostAsync(Session, v, _cancellationToken));
		if (_publishRefs) await RunAsync(() => _publisher.PublishRefsAsync(Session, v, _cancellationToken));
		if (_publishBans) await RunAsync(() => _publisher.PublishBansAsync(Session, v, _cancellationToken));
		if (_publishTimer)
			try
			{
				_publisher.PublishTimer(Session, v);
			}
			catch
			{
				// A publish failure never fails the mutation: the state change already committed to
				// memory, and the live stream is a projection of it rather than a participant in it.
			}
	}

	private static async Task RunAsync(Func<Task> publish)
	{
		try
		{
			await publish();
		}
		catch
		{
			// A publish failure never fails the mutation: the state change already committed to
			// memory, and the live stream is a projection of it rather than a participant in it.
		}
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync() => await CompleteAsync();
}
