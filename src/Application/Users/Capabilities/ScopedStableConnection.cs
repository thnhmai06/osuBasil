using Basil.Application.Shared.Privileges;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Client;
using Basil.Domain.Users;

namespace Basil.Application.Users.Capabilities;

/// <summary>An osu! client as one connection may act on it.</summary>
public readonly struct ScopedStableConnection
{
	/// <summary>Gets the actor this scope is for.</summary>
	public Connection Actor { get; }

	/// <summary>Gets the privileges the actor held when the scope was created.</summary>
	public UserPrivileges Privileges { get; }

	/// <summary>Checks whether the actor holds the privilege an action needs.</summary>
	/// <param name="required">The privilege the action needs.</param>
	/// <returns><see langword="null" /> when it is held; otherwise what is missing.</returns>
	internal bool Lacks(UserPrivileges required)
	{
		return ScopedPrivileges.Lacks(Privileges, required);
	}

	/// <summary>Creates a scope for an osu! client and a connection.</summary>
	/// <param name="host">The client to scope.</param>
	/// <param name="actor">The connection acting on the client.</param>
	internal ScopedStableConnection(StableConnection host, Connection actor)
	{
		Actor = actor;
		Privileges = actor.EffectivePrivileges(host.Context.Clock.GetUtcNow());
		Host = host;
	}

	/// <summary>Gets the osu! client this scope is over.</summary>
	public StableConnection Host { get; }

	/// <summary>Starts spectating this client.</summary>
	/// <returns>The outcome, or the privileges the caller lacks.</returns>
	/// <remarks>
	///     Needs <see cref="UserPrivileges.Player" />, which <see cref="UserPrivileges.Tournament" /> includes. A
	///     host's block of the spectator prevents spectating unless the spectator has
	///     <see cref="UserPrivileges.Tournament" /> in effect. The spectator joins the host's spectator channel, and the
	///     first spectator brings the host into it too. A connection spectating another client stops spectating it first.
	///     Spectating this host again announces the spectator, as an osu! client does once it has the beatmap.
	/// </remarks>
	/// <exception cref="ArgumentException">
	///     <see cref="Actor" /> is an IRC connection, which cannot
	///     spectate.
	/// </exception>
	public async Task<ConnectionOutcome> SpectateAsync()
	{
		if (Actor is IrcConnection)
			throw new ArgumentException("IRC clients cannot spectate.");

		if (Lacks(UserPrivileges.Player)) return ConnectionOutcome.Denied;

		if (await Host.HasBlockedAsync(Actor) && Lacks(UserPrivileges.Tournament))
			return ConnectionOutcome.Blocked;

		return Host.Spectate(Actor);
	}

	/// <summary>Stops this connection spectating this host.</summary>
	/// <returns><see langword="true" /> if the connection stopped spectating.</returns>
	/// <remarks>Calling it again does nothing.</remarks>
	public bool StopSpectating()
	{
		return Host.StopSpectating(Actor);
	}

	/// <summary>Reports that this connection failed to spectate this host, for example because it lacks the beatmap.</summary>
	/// <returns><see langword="true" /> if the connection is spectating this host.</returns>
	public bool ReportSpectatingFailed()
	{
		return Host.ReportSpectatingFailed(Actor);
	}

	/// <summary>Hands the frames this client sent to the HTTP API connections spectating it.</summary>
	/// <param name="bundle">The decoded frames.</param>
	/// <returns>
	///     <see langword="true" /> if the frames were published; only the client itself may, and only while an API
	///     connection spectates it.
	/// </returns>
	public bool PublishFrames(SpectatorBundle bundle)
	{
		return ReferenceEquals(Actor, Host) && Host.PublishFrames(bundle);
	}

	/// <summary>Records the presence status this client reported.</summary>
	/// <param name="status">The reported status.</param>
	/// <returns><see langword="true" /> if the status changed.</returns>
	/// <remarks>A different connection, a closed client, or its current status does nothing.</remarks>
	public bool SetStatus(PlayerStatus status)
	{
		return ReferenceEquals(Actor, Host) && Host.SetStatus(status);
	}
}