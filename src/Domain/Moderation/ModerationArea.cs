using Basil.Domain.Users;

namespace Basil.Domain.Moderation;

/// <summary>The permissions of one area and the permission that manages it.</summary>
/// <param name="Permissions">The permissions the area covers.</param>
/// <param name="Manager">
///     The permission a user must hold in full to keep the area's permissions in effect while they are restricted;
///     <see cref="UserPermissions.None" /> means nobody is exempt.
/// </param>
/// <remarks>A restriction never suspends the permissions of an area from a user who holds the area's manager.</remarks>
public readonly record struct ModerationArea(UserPermissions Permissions, UserPermissions Manager);