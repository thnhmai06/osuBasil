using Basil.Application.Models.Multiplayer;
using Basil.Domain.Users;

namespace Basil.Application.Models.Notifications;

/// <summary>The recipient was invited to a room.</summary>
/// <param name="Room">The room the recipient was invited to.</param>
/// <param name="From">The user who extended the invitation, or <see langword="null" /> when unknown.</param>
public sealed record Invited(Room Room, User? From) : Notification;