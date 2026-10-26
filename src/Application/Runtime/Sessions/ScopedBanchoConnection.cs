using Basil.Application.Persistence.Scores;
using Basil.Application.Runtime.Common;
using Basil.Application.Runtime.Scores;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Runtime.Sessions;

/// <summary>An osu! client as one connection may act on it.</summary>
public sealed class ScopedBanchoConnection : Scoped<UserPermissions, Connection>
{
	/// <summary>Creates a scope for an osu! client and a connection.</summary>
	/// <param name="host">The client to scope.</param>
	/// <param name="by">The connection acting on the client.</param>
	internal ScopedBanchoConnection(BanchoConnection host, Connection by) : base(by,
		by.User.EffectivePermissions(host.Context.Clock.GetUtcNow()))
	{
		Host = host;
	}

	/// <summary>Gets the osu! client this scope is over.</summary>
	public BanchoConnection Host { get; }

	/// <summary>Starts spectating this client.</summary>
	/// <returns>The outcome, or the permissions the caller lacks.</returns>
	/// <remarks>
	///     Needs <see cref="UserPermissions.SpectatingWatch" />, which <see cref="UserPermissions.RoomObserve" /> includes. A
	///     host's block of the spectator prevents spectating unless the spectator has
	///     <see cref="UserPermissions.RoomObserve" /> in effect. The spectator joins the host's spectator channel, and the
	///     first spectator brings the host into it too. A connection spectating another client stops spectating it first.
	///     Spectating this host again announces the spectator, as an osu! client does once it has the beatmap.
	/// </remarks>
	/// <exception cref="ArgumentException"><see cref="Scoped{TPermissions,TActor}.By" /> is an IRC connection, which cannot spectate.</exception>
	public async Task<Result<ConnectionOutcome>> SpectateAsync()
	{
		if (By is IrcConnection)
			throw new ArgumentException("IRC clients cannot spectate.");

		if (Require(UserPermissions.SpectatingWatch) is { } missing) return Result<ConnectionOutcome>.Denied(missing);

		var relationships = await Host.Context.Relationships.ListAsync(Host.Record);
		if (relationships.Any(relationship => relationship.Type == RelationshipType.Block &&
		                                      relationship.Target.Equals(By.Record)) &&
		    Require(UserPermissions.RoomObserve) is not null)
			return ConnectionOutcome.Blocked;

		// Leave the previous host before entering this host's scope, so two spectator scopes are never held at once.
		var previous = Host.Context.Registry.FindSpectating(By);
		if (previous is not null && !ReferenceEquals(previous, Host)) previous.ScopeTo(By).StopSpectating();

		using var scope = Host.EnterSpectators();

		if (!Host.IsOpen) return ConnectionOutcome.TargetOffline;
		// The connection may have closed during the awaits above; check it again before the spectator is added.
		if (!By.IsOpen) return ConnectionOutcome.Closed;
		if (By.Record.Equals(Host.Record)) return ConnectionOutcome.Self;
		if (Host.Spectators.Contains(By))
		{
			Host.Context.Events.Publish(new UserSpectatorJoined(Host, By));
			return ConnectionOutcome.Ok;
		}

		var first = Host.Spectators.Count == 0;
		Host.MutableSpectators.Add(By);
		if (first) Host.SpectatorChannel.ScopeTo(Host).Join();
		Host.SpectatorChannel.ScopeTo(By).Join();
		Host.Context.Events.Publish(new UserSpectatorJoined(Host, By));
		return ConnectionOutcome.Ok;
	}

	/// <summary>Stops this connection spectating this host.</summary>
	/// <returns><see langword="true" /> if the connection stopped spectating.</returns>
	/// <remarks>Calling it again does nothing.</remarks>
	public bool StopSpectating()
	{
		using var scope = Host.EnterSpectators();
		if (!Host.MutableSpectators.Remove(By)) return false;

		Host.SpectatorChannel.ScopeTo(By).Part();
		if (Host.Spectators.Count == 0) Host.SpectatorChannel.ScopeTo(Host).Part();
		Host.Context.Events.Publish(new UserSpectatorLeft(Host, By));
		return true;
	}

	/// <summary>Reports that this connection failed to spectate this host, for example because it lacks the beatmap.</summary>
	/// <returns><see langword="true" /> if the connection is spectating this host.</returns>
	public bool ReportSpectatingFailed()
	{
		if (!Host.Spectators.Contains(By)) return false;

		Host.Context.Events.Publish(new UserSpectatorFailed(Host, By));
		return true;
	}

	/// <summary>Records the presence status this client reported.</summary>
	/// <param name="status">The reported status.</param>
	/// <returns><see langword="true" /> if the status changed.</returns>
	/// <remarks>A different connection, a closed client, or its current status does nothing.</remarks>
	public bool SetStatus(PlayerStatus status)
	{
		if (!ReferenceEquals(By, Host) || !Host.IsOpen || Equals(Host.Status, status)) return false;

		Host.Status = status;
		Host.Context.Events.Publish(new UserConnectionStatusChanged(Host, status));
		return true;
	}

	/// <summary>Validates and records the score this game client submitted.</summary>
	/// <param name="submission">The parsed submission.</param>
	/// <param name="beatmap">
	///     The beatmap the server knows for the submission, or <see langword="null" /> when the server does
	///     not have it.
	/// </param>
	/// <param name="client">The osu! client information sent with the submission.</param>
	/// <param name="replay">
	///     The seekable stream of the replay, positioned at its start and left open, or <see langword="null" /> for a
	///     failed play.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the submission.</param>
	/// <returns>
	///     Ok when the score was stored; otherwise the outcome (<see cref="ConnectionOutcome.Rejected" /> carries the
	///     rejection) or the permissions the caller lacks.
	/// </returns>
	/// <remarks>
	///     Needs <see cref="UserPermissions.ScoreSubmit" />, and only the client itself submits its own scores. A score on a
	///     beatmap the server does not have is accepted only when it is the beatmap of a round of an open room that still
	///     awaits a score from the player (a player who completed and then
	///     left is still awaited). Every accepted play adds to the play count and the total score; passed plays also add to
	///     the ranked score. The submission is checked against the player's latest recorded login. A submission whose
	///     checksum was already stored is refused. Only a passed play keeps its replay, and only when the replay is at
	///     least 24 bytes long.
	/// </remarks>
	public async Task<(Result<ConnectionOutcome> Result, ScoreRejection? Rejection)> SubmitScoreAsync(
		Submission submission,
		BeatmapChecksums? beatmap,
		SubmittedClient client,
		Stream? replay,
		CancellationToken cancellationToken = default)
	{
		if (Require(UserPermissions.ScoreSubmit) is { } missing)
			return (Result<ConnectionOutcome>.Denied(missing), null);
		if (!ReferenceEquals(By, Host)) return (ConnectionOutcome.OtherClient, null);

		var checkedBeatmap = beatmap ??
		                     (Host.Context.Lobby.AwaitsScore(Host.Record, client.BeatmapHash)
			                     ? new BeatmapChecksums(client.BeatmapHash, null)
			                     : null);
		if (checkedBeatmap is null) return (ConnectionOutcome.Rejected, ScoreRejection.UnknownBeatmap);

		var outcome = await Host.Context.Scores.ValidateAndRecordAsync(
			Host.Record, Host.Login.Client!, submission, checkedBeatmap, client, replay, cancellationToken);
		if (outcome.Rejection is { } rejection) return (ConnectionOutcome.Rejected, rejection);

		Host.Context.ScoreEvents.Publish(new ScoreSubmitted(Host.Record, outcome.Score!, outcome.Stats!));
		return (ConnectionOutcome.Ok, null);
	}
}