using Basil.Application.Scores.Messages;
using Basil.Application.Sessions.Objects.Events;
using Basil.Application.Sessions.Registry;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Events;
using Basil.Domain.Sessions;
using Basil.Domain.Game;
using Basil.Host.Stable.Support;
using Mediator;

namespace Basil.Host.Stable.Delivery;

internal sealed class UserOnlineDelivery(
	PresenceAudience audience,
	Outbox outbox,
	ISender sender,
	TimeProvider clock) : INotificationHandler<SessionOpened>
{
	public async ValueTask Handle(SessionOpened notification, CancellationToken ct)
	{
		if (!notification.CameOnline || notification.Session.Type is not SessionType.Stable) return;
		var user = notification.Session.Siblings;
		var stats = await sender.Send(new GetUserStats(user.User, user.Stable?.Status.Detail?.Mode ?? GameMode.Standard), ct);
		var now = clock.GetUtcNow();
		outbox.Send(audience.For(user), writer =>
		{
			StableView.WritePresence(writer, user, stats, now);
			StableView.WriteStats(writer, user, stats);
		});
	}
}

internal sealed class UserOfflineDelivery(SessionRegistry registry, Outbox outbox)
	: INotificationHandler<SessionClosed>
{
	public ValueTask Handle(SessionClosed notification, CancellationToken ct)
	{
		var connection = notification.Session;
		outbox.Remove(connection);
		if (notification.WentOffline && connection.Type is SessionType.Stable)
			outbox.Send(registry.OpenStableClients(), writer => writer.Logout(connection.Record.Id));

		return ValueTask.CompletedTask;
	}
}

internal sealed class UserStatusDelivery(PresenceAudience audience, Outbox outbox, ISender sender)
	: INotificationHandler<SessionStatusChanged>
{
	public async ValueTask Handle(SessionStatusChanged notification, CancellationToken ct)
	{
		var user = notification.Session.Siblings;
		var stats = await sender.Send(new GetUserStats(user.User, notification.Status.Detail?.Mode ?? GameMode.Standard), ct);
		outbox.Send(audience.For(user), writer => StableView.WriteStats(writer, user, stats));
	}
}

internal sealed class UserNotificationDelivery(Outbox outbox) : INotificationHandler<SessionNotificationSent>
{
	public ValueTask Handle(SessionNotificationSent notification, CancellationToken ct)
	{
		outbox.Send(notification.Recipients, writer => writer.Notification(notification.Text));
		return ValueTask.CompletedTask;
	}
}

internal sealed class UserSilencedDelivery(SessionRegistry registry, Outbox outbox, TimeProvider clock)
	: INotificationHandler<UserSilenced>
{
	public ValueTask Handle(UserSilenced notification, CancellationToken ct)
	{
		var seconds = Math.Max(0, (int)(notification.Until - clock.GetUtcNow()).TotalSeconds);
		outbox.Send(registry.Of(notification.User).Connections, writer => writer.SilenceEnd(seconds));
		return ValueTask.CompletedTask;
	}
}

internal sealed class UserUnsilencedDelivery(SessionRegistry registry, Outbox outbox)
	: INotificationHandler<UserUnsilenced>
{
	public ValueTask Handle(UserUnsilenced notification, CancellationToken ct)
	{
		outbox.Send(registry.Of(notification.User).Connections, writer => writer.SilenceEnd(0));
		return ValueTask.CompletedTask;
	}
}

internal sealed class UserPrivilegesDelivery(SessionRegistry registry, Outbox outbox, TimeProvider clock)
	: INotificationHandler<UserPrivilegesChanged>
{
	public ValueTask Handle(UserPrivilegesChanged notification, CancellationToken ct)
	{
		var user = registry.Of(notification.User);
		var privileges = StableView.Privileges(user.EffectivePrivileges(clock.GetUtcNow()));
		outbox.Send(user.Connections, writer => writer.Privileges(privileges));
		return ValueTask.CompletedTask;
	}
}

internal sealed class SpectatorJoinedDelivery(Outbox outbox) : INotificationHandler<SpectatorJoined>
{
	public ValueTask Handle(SpectatorJoined notification, CancellationToken ct)
	{
		var (host, spectator) = (notification.Host, notification.Spectator);
		// A player spectating themselves is not shown in the game client.
		if (spectator.Record.Equals(host.Record)) return ValueTask.CompletedTask;

		var others = host.Spectators.Where(other => !ReferenceEquals(other, spectator)).ToList();
		outbox.Send(host, writer => writer.SpectatorJoined(spectator.Record.Id));
		outbox.Send(others, writer => writer.FellowSpectatorJoined(spectator.Record.Id));
		outbox.Send(spectator, writer =>
		{
			foreach (var other in others) writer.FellowSpectatorJoined(other.Record.Id);
		});
		return ValueTask.CompletedTask;
	}
}

internal sealed class SpectatorLeftDelivery(Outbox outbox) : INotificationHandler<SpectatorLeft>
{
	public ValueTask Handle(SpectatorLeft notification, CancellationToken ct)
	{
		var (host, spectator) = (notification.Host, notification.Spectator);
		if (spectator.Record.Equals(host.Record)) return ValueTask.CompletedTask;

		outbox.Send(host, writer => writer.SpectatorLeft(spectator.Record.Id));
		outbox.Send(host.Spectators, writer => writer.FellowSpectatorLeft(spectator.Record.Id));
		return ValueTask.CompletedTask;
	}
}

internal sealed class SpectatorFailedDelivery(Outbox outbox) : INotificationHandler<SpectatorFailed>
{
	public ValueTask Handle(SpectatorFailed notification, CancellationToken ct)
	{
		outbox.Send(notification.Host, writer => writer.SpectatorCantSpectate(notification.Spectator.Record.Id));
		return ValueTask.CompletedTask;
	}
}