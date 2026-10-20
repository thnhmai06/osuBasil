using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Sessions;

/// <summary>An osu! client scoped to one connection.</summary>
public sealed class ScopedBanchoConnection
{
	/// <summary>The osu! client this scope is over.</summary>
	public BanchoConnection Host { get; }

	/// <summary>The connection acting on this client.</summary>
	public Connection By { get; }

	/// <summary>Creates a scope for an osu! client and a connection.</summary>
	/// <param name="host">The client to scope.</param>
	/// <param name="by">The connection acting on the client.</param>
	internal ScopedBanchoConnection(BanchoConnection host, Connection by)
	{
		Host = host;
		By = by;
	}

	/// <summary>Starts spectating this client.</summary>
	/// <returns>The outcome.</returns>
	/// <remarks>
	///     Needs <see cref="Permissions.SpectatingWatch" />, which <see cref="Permissions.RoomObserve" /> includes;
	///     NotPermitted otherwise. A host's block of the spectator prevents spectating unless the spectator has
	///     <see cref="Permissions.RoomObserve" /> in effect. The spectator joins the host's spectator channel, and the
	///     first spectator brings the host into it too. A connection spectating another client stops spectating it first.
	///     Spectating this host again announces the spectator, as an osu! client does once it has the beatmap.
	/// </remarks>
	/// <exception cref="ArgumentException"><see cref="By" /> is an IRC connection, which cannot spectate.</exception>
	public async Task<SpectateResult> SpectateAsync()
	{
		if (By is IrcConnection)
			throw new ArgumentException("IRC clients cannot spectate.");

		var now = Host.Context.Clock.GetUtcNow();
		if (By.Session.Check(Permissions.SpectatingWatch, now) is not Access.Allowed)
			return SpectateResult.NotPermitted;

		var relationships = await Host.Context.Relationships.ListAsync(Host.User);
		if (relationships.Any(relationship => relationship.Type == RelationshipType.Block &&
		                                      relationship.Target.Equals(By.User)) &&
		    By.Session.Check(Permissions.RoomObserve, now) is not Access.Allowed)
			return SpectateResult.Blocked;

		// Leave the previous host before entering this host's scope, so two spectator scopes are never held at once.
		var previous = Host.Context.Registry.FindSpectating(By);
		if (previous is not null && !ReferenceEquals(previous, Host)) previous.ScopeTo(By).StopSpectating();

		using var scope = Host.EnterSpectators();

		if (!Host.IsOpen) return SpectateResult.TargetOffline;
		if (By.User.Equals(Host.User)) return SpectateResult.Self;
		if (Host.Spectators.Contains(By))
		{
			Host.Context.Events.Emit(new UserSpectatorJoined(Host, By));
			return SpectateResult.Spectating;
		}

		var first = Host.Spectators.Count == 0;
		Host.MutableSpectators.Add(By);
		if (first) Host.SpectatorChannel.ScopeTo(Host).Join();
		Host.SpectatorChannel.ScopeTo(By).Join();
		Host.Context.Events.Emit(new UserSpectatorJoined(Host, By));
		return SpectateResult.Spectating;
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
		Host.Context.Events.Emit(new UserSpectatorLeft(Host, By));
		return true;
	}

	/// <summary>Reports that this connection failed to spectate this host, for example because it lacks the beatmap.</summary>
	/// <returns><see langword="true" /> if the connection is spectating this host.</returns>
	public bool ReportSpectatingFailed()
	{
		if (!Host.Spectators.Contains(By)) return false;

		Host.Context.Events.Emit(new UserSpectatorFailed(Host, By));
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
		Host.Context.Events.Emit(new UserConnectionStatusChanged(Host, status));
		return true;
	}
}

/// <summary>The outcome of starting to spectate a player.</summary>
public enum SpectateResult : byte
{
	/// <summary>The connection is now spectating the host.</summary>
	Spectating,

	/// <summary>The host's connection is closed.</summary>
	TargetOffline,

	/// <summary>A user cannot spectate themselves.</summary>
	Self,

	/// <summary>The spectator is not granted the permission to spectate, or it is suspended.</summary>
	NotPermitted,

	/// <summary>The host blocks the spectator.</summary>
	Blocked
}