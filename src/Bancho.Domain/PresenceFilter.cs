namespace Bancho.Domain;

/// <summary>Ported from app/objects/player.py's PresenceFilter — client-side filter for which users the player can see.</summary>
public enum PresenceFilter
{
    Nil = 0,
    All = 1,
    Friends = 2
}