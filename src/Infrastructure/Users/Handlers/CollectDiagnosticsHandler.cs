using Basil.Application.Shared.Diagnostics;
using Basil.Application.Users.Registry;
using Basil.Domain.Authentication;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Reports the users online and the open connections of each type; the bot's own connections are not clients.</summary>
internal sealed class CollectDiagnosticsHandler(UserRegistry registry) : INotificationHandler<CollectDiagnostics>
{
	/// <inheritdoc />
	public ValueTask Handle(CollectDiagnostics notification, CancellationToken ct)
	{
		var collector = notification.Collector;
		collector.Add(new DiagnosticsValue("application", "usersOnline", registry.Online.Count));
		foreach (var type in Enum.GetValues<SessionType>())
			collector.Add(new DiagnosticsValue("sessions", type.ToString().ToLowerInvariant(), Count(type)));

		collector.Add(new DiagnosticsValue("stable", "osu", Count(SessionType.Stable)));
		collector.Add(new DiagnosticsValue("stable", "tourney", Count(SessionType.StableTourney)));
		collector.Add(new DiagnosticsValue("irc", "connections", Count(SessionType.Irc)));
		return ValueTask.CompletedTask;
	}

	private int Count(SessionType type)
	{
		return registry.Online.Values.Sum(user => user.Connections.Count(c => c.Type == type && !c.IsServerOwned));
	}
}