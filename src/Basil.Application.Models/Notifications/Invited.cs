using Basil.Domain.Multiplayer.Runtime;

namespace Basil.Application.Models.Notifications;

/// <summary>The recipient was invited to a room.</summary>
public sealed record Invited(Room Room, int FromUserId) : Notification;