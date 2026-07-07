namespace OpenOsuTournament.Bancho.Domain.Users;

/// <summary>Ported from app/constants/privileges.py's Privileges (IntFlag) — server-side user privileges.</summary>
[Flags]
public enum Privileges
{
    Unrestricted = 1 << 0,
    Verified = 1 << 1,
    Whitelisted = 1 << 2,
    Supporter = 1 << 4,
    Premium = 1 << 5,
    Alumni = 1 << 7,
    TourneyManager = 1 << 10,
    Nominator = 1 << 11,
    Moderator = 1 << 12,
    Administrator = 1 << 13,
    Developer = 1 << 14,

    Donator = Supporter | Premium,
    Staff = Moderator | Administrator | Developer
}

/// <summary>Ported from app/constants/privileges.py's ClientPrivileges (IntFlag) — client-side user privileges.</summary>
[Flags]
public enum ClientPrivileges
{
    Player = 1 << 0,
    Moderator = 1 << 1,
    Supporter = 1 << 2,
    Owner = 1 << 3,
    Developer = 1 << 4,
    Tournament = 1 << 5 // NOTE: not used in communications with osu! client
}

/// <summary>Ported from app/constants/privileges.py's ClanPrivileges (IntEnum).</summary>
public enum ClanPrivileges
{
    Member = 1,
    Officer = 2,
    Owner = 3
}