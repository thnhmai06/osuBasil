namespace Basil.Application.Models.Notifications;

/// <summary>
///     The transport-neutral language Application uses to tell a client something happened. A
///     <see cref="Ports.IClientConnection" /> is responsible for encoding a <see cref="Notification" />
///     into whatever wire format its transport uses, or dropping it when the transport has no
///     equivalent.
/// </summary>
public abstract record Notification;