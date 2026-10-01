using Basil.Application.Chat;
using Basil.Domain.Chat;

namespace Basil.Application.Sessions;

/// <summary>The chat channel of an osu! client and the users spectating it.</summary>
public sealed class SpectatorChatChannelSession(BanchoConnection host, TimeProvider time)
	: ChatChannelSession(new SpectatorChatChannel(host.User), time)
{
	/// <summary>Gets the connection being spectated.</summary>
	public BanchoConnection Host => host;

	/// <summary>Gets the connections spectating the host.</summary>
	public IEnumerable<Connection> Spectators => Members.Where(member => !ReferenceEquals(member, Host));

	/// <inheritdoc />
	/// <remarks>The host and its spectators read the channel while it has spectators.</remarks>
	public override bool CanRead(Connection connection)
	{
		return Members.Contains(connection);
	}

	/// <inheritdoc />
	public override bool CanWrite(Connection connection)
	{
		return CanRead(connection);
	}

	/// <summary>Starts spectating the host; the first spectator brings the host into the channel.</summary>
	/// <param name="by">The osu! or osu!tourney client that starts spectating.</param>
	/// <returns>The outcome.</returns>
	/// <exception cref="ArgumentException"><paramref name="by" /> is not an osu! or osu!tourney client.</exception>
	public SpectateResult Spectate(Connection by)
	{
		if (by is not (BanchoConnection or TourneyConnection))
			throw new ArgumentException("Only osu! and osu!tourney clients can spectate.", nameof(by));

		lock (Sync)
		{
			if (!Host.IsOpen) return SpectateResult.TargetOffline;
			if (by.User.Equals(Host.User)) return SpectateResult.Self;
			if (Members.Contains(by)) return SpectateResult.AlreadySpectating;

			var hostJoined = AddMember(Host);
			AddMember(by);
			Emit(new SpectatorJoined(this, by, hostJoined));
			return SpectateResult.Spectating;
		}
	}

	/// <summary>Stops spectating the host; the last spectator takes the host out of the channel.</summary>
	/// <param name="by">The spectator that stops.</param>
	/// <returns>
	///     <see langword="true" /> if the connection was spectating; calling it again returns <see langword="false" />
	///     and does nothing.
	/// </returns>
	public bool StopSpectating(Connection by)
	{
		lock (Sync)
		{
			if (ReferenceEquals(by, Host) || !RemoveMember(by)) return false;

			var hostLeft = !Spectators.Any() && RemoveMember(Host);
			Emit(new SpectatorLeft(this, by, hostLeft));
			return true;
		}
	}

	/// <summary>Reports that a spectator cannot spectate the host.</summary>
	/// <param name="by">The spectator reporting it.</param>
	/// <returns>
	///     <see langword="true" /> if the connection is spectating; otherwise, <see langword="false" /> and nothing is
	///     reported.
	/// </returns>
	public bool CantSpectate(Connection by)
	{
		if (ReferenceEquals(by, Host) || !Members.Contains(by)) return false;

		Emit(new SpectatorCantSpectate(this, by));
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

	/// <summary>The connection is already spectating this host.</summary>
	AlreadySpectating
}