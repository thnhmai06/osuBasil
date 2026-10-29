using Basil.Application.Multiplayer.Runtime;
using Basil.Application.Users.Users;
using Basil.Domain.Auth;

namespace Basil.Host.Shared.Diagnostics;

/// <summary>The one sample of every diagnostics category, which the HTTP API streams and the dashboard draws.</summary>
public static class DiagnosticsSample
{
	/// <summary>The names of the categories, in the order the API lists them.</summary>
	public static readonly string[] Categories =
		["process", "gc", "threadpool", "runtime", "exceptions", "http", "application", "database", "bancho", "irc", "events", "sessions"];

	/// <summary>Takes the sample of the second.</summary>
	/// <param name="runtime">The sampler of the process.</param>
	/// <param name="registry">The users online.</param>
	/// <param name="lobby">The rooms.</param>
	/// <param name="openStreams">How many live HTTP streams are open.</param>
	/// <returns>The value of each category.</returns>
	public static Dictionary<string, object> Create(RuntimeSampler runtime, UserRegistry registry, Lobby lobby, int openStreams)
	{
		int Count(ConnectionType type)
		{
			return registry.Online.Values.Sum(user => user.Connections.Count(c => c.Type == type));
		}

		return new Dictionary<string, object>(runtime.Sample())
		{
			["application"] = new
			{
				usersOnline = registry.Online.Count, rooms = lobby.Rooms.Count,
				roundsInProgress = lobby.Rooms.Values.Count(room => room.Rounds.InProgress), openStreams
			},
			["sessions"] = Enum.GetValues<ConnectionType>().ToDictionary(type => type.ToString().ToLowerInvariant(), Count),
			["bancho"] = new { osu = Count(ConnectionType.Osu), tourney = Count(ConnectionType.Tourney) },
			["irc"] = new { connections = Count(ConnectionType.Irc) },
			["database"] = new { available = false },
			["events"] = new { available = false }
		};
	}
}
