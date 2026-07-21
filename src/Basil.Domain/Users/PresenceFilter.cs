namespace Basil.Domain.Users;

/// <summary>Ported from app/objects/player.py's PresenceFilter — client-side filter for which users the player can see.</summary>
public enum PresenceFilter : byte
{
    Nil = 0,
    All = 1,
    Friends = 2
}