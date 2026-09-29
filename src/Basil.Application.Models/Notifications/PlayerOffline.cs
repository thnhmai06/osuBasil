using Basil.Domain.Users;

namespace Basil.Application.Models.Notifications;

/// <summary>A user went offline.</summary>
/// <param name="User">The user who went offline.</param>
public sealed record PlayerOffline(User User) : Notification;