namespace Basil.Application.Models.Notifications;

/// <summary>A player in the recipient's room skipped the current beatmap's intro.</summary>
public sealed record PlayerSkipped(int Slot) : Notification;