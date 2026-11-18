using Basil.Application.Shared.Diagnostics;
using Basil.Application.Users.Objects.Sessions;
using Basil.Application.Users.Registry;
using Basil.Domain.Authentication;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class CollectDiagnosticsHandler(UserRegistry registry) : INotificationHandler<CollectDiagnostics>
{
	public ValueTask Handle(CollectDiagnostics notification, CancellationToken ct)
	{
		var collector = notification.Collector;
		collector.Add(new DiagnosticsValue("application", "usersOnline", registry.Online.Count));
		foreach (var type in Enum.GetValues<SessionType>())
			collector.Add(new DiagnosticsValue("sessions", type.ToString().ToLowerInvariant(), Count(type)));

		foreach (var type in Enum.GetValues<SessionType>())
		foreach (var kind in Enum.GetValues<SessionCount>())
			collector.Add(new DiagnosticsValue("connections",
				$"{type.ToString().ToLowerInvariant()}.{kind.ToString().ToLowerInvariant()}",
				SessionMetrics.Total(type, kind)));

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