namespace Basil.Application.Models.Notifications;

/// <summary>A room countdown ticked past a milestone.</summary>
public sealed record CountdownTick(TimeSpan Remaining) : Notification;