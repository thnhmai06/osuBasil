using Basil.Application.Scores.Persistence;
using Basil.Application.Users.Objects.Events;
using Basil.Application.Users.Registry;
using Basil.Domain.Auth;
using Basil.Domain.Mechanics;
using Basil.Host.Bancho.Support;
using Mediator;

namespace Basil.Host.Bancho.Delivery;

/// <summary>Tells every osu! client that a user came online.</summary>
internal sealed class UserOnlineDelivery(
	UserRegistry registry,
	Outbox outbox,
	IUserStatsRepository statsRepository,
	TimeProvider clock) : INotificationHandler<UserConnectionOpened>
{
	public async ValueTask Handle(UserConnectionOpened notification, CancellationToken cancellationToken)
	{
		if (!notification.CameOnline || notification.Connection.Type is not (SessionType.Osu or SessionType.Bot))
			return;
		var user = notification.Connection.User;
		var stats = await statsRepository.GetAsync(user.Record, user.Bancho?.Status.GameMode ?? GameMode.Standard,
			cancellationToken);
		var now = clock.GetUtcNow();
		outbox.Send(registry.OpenBanchoClients(), writer =>
		{
			BanchoView.WritePresence(writer, user, stats, now);
			BanchoView.WriteStats(writer, user, stats);
		});
	}
}

/// <summary>Tells every osu! client that a user went offline.</summary>
internal sealed class UserOfflineDelivery(UserRegistry registry, Outbox outbox)
	: INotificationHandler<UserConnectionClosed>
{
	public ValueTask Handle(UserConnectionClosed notification, CancellationToken cancellationToken)
	{
		var connection = notification.Connection;
		outbox.Remove(connection);
		if (notification.WentOffline && connection.Type is SessionType.Osu)
			outbox.Send(registry.OpenBanchoClients(), writer => writer.Logout(connection.Record.Id));

		return ValueTask.CompletedTask;
	}
}

/// <summary>Tells every osu! client the new status of a user.</summary>
internal sealed class UserStatusDelivery(UserRegistry registry, Outbox outbox, IUserStatsRepository statsRepository)
	: INotificationHandler<UserConnectionStatusChanged>
{
	public async ValueTask Handle(UserConnectionStatusChanged notification, CancellationToken cancellationToken)
	{
		var user = notification.Connection.User;
		var stats = await statsRepository.GetAsync(user.Record, notification.Status.GameMode, cancellationToken);
		outbox.Send(registry.OpenBanchoClients(), writer => BanchoView.WriteStats(writer, user, stats));
	}
}

/// <summary>Shows a notification on the osu! clients the server addressed.</summary>
internal sealed class UserNotificationDelivery(Outbox outbox) : INotificationHandler<UserNotificationSent>
{
	public ValueTask Handle(UserNotificationSent notification, CancellationToken cancellationToken)
	{
		outbox.Send(notification.Recipients, writer => writer.Notification(notification.Text));
		return ValueTask.CompletedTask;
	}
}

/// <summary>Tells a user's osu! client that the account was restricted.</summary>
internal sealed class UserRestrictedDelivery(UserRegistry registry, Outbox outbox, TimeProvider clock)
	: INotificationHandler<UserRestricted>
{
	public ValueTask Handle(UserRestricted notification, CancellationToken cancellationToken)
	{
		var user = registry.Of(notification.User);
		var privileges = BanchoView.Privileges(user.EffectivePermissions(clock.GetUtcNow()));
		outbox.Send(user.Connections, writer =>
		{
			writer.AccountRestricted();
			writer.Privileges(privileges);
		});
		return ValueTask.CompletedTask;
	}
}

/// <summary>Sends a user's osu! clients their new role bits.</summary>
internal sealed class UserPermissionsDelivery(UserRegistry registry, Outbox outbox, TimeProvider clock)
	: INotificationHandler<UserPermissionsChanged>
{
	public ValueTask Handle(UserPermissionsChanged notification, CancellationToken cancellationToken)
	{
		var user = registry.Of(notification.User);
		var privileges = BanchoView.Privileges(user.EffectivePermissions(clock.GetUtcNow()));
		outbox.Send(user.Connections, writer => writer.Privileges(privileges));
		return ValueTask.CompletedTask;
	}
}

/// <summary>Tells a host and its fellow spectators that someone started spectating.</summary>
internal sealed class SpectatorJoinedDelivery(Outbox outbox) : INotificationHandler<UserSpectatorJoined>
{
	public ValueTask Handle(UserSpectatorJoined notification, CancellationToken cancellationToken)
	{
		var (host, spectator) = (notification.Host, notification.Spectator);
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

/// <summary>Tells a host and its fellow spectators that someone stopped spectating.</summary>
internal sealed class SpectatorLeftDelivery(Outbox outbox) : INotificationHandler<UserSpectatorLeft>
{
	public ValueTask Handle(UserSpectatorLeft notification, CancellationToken cancellationToken)
	{
		var (host, spectator) = (notification.Host, notification.Spectator);
		outbox.Send(host, writer => writer.SpectatorLeft(spectator.Record.Id));
		outbox.Send(host.Spectators, writer => writer.FellowSpectatorLeft(spectator.Record.Id));
		return ValueTask.CompletedTask;
	}
}

/// <summary>Tells a host that a spectator lacks the beatmap.</summary>
internal sealed class SpectatorFailedDelivery(Outbox outbox) : INotificationHandler<UserSpectatorFailed>
{
	public ValueTask Handle(UserSpectatorFailed notification, CancellationToken cancellationToken)
	{
		outbox.Send(notification.Host, writer => writer.SpectatorCantSpectate(notification.Spectator.Record.Id));
		return ValueTask.CompletedTask;
	}
}