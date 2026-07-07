namespace OpenOsuTournament.Bancho.Domain.Beatmaps;

/// <summary>Ported from app/constants/beatmap_statuses.py's RankedStatus (IntEnum).</summary>
public enum RankedStatus
{
    NotSubmitted = -1,
    Pending = 0,
    UpdateAvailable = 1,
    Ranked = 2,
    Approved = 3,
    Qualified = 4,
    Loved = 5
}

/// <summary>Ported from app/constants/beatmap_statuses.py's RankedStatus classmethods/properties actually used by Phase 5.</summary>
public static class RankedStatusExtensions
{
    /// <summary>Ported from RankedStatus.osu_api — only statuses osu!api itself returns are mapped.</summary>
    public static int OsuApi(this RankedStatus status)
    {
        return status switch
        {
            RankedStatus.Pending => 0,
            RankedStatus.Ranked => 1,
            RankedStatus.Approved => 2,
            RankedStatus.Qualified => 3,
            RankedStatus.Loved => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "No osu!api mapping for this status.")
        };
    }

    /// <summary>Ported from RankedStatus.from_osuapi (defaultdict falls back to UpdateAvailable).</summary>
    public static RankedStatus FromOsuApi(int osuApiStatus)
    {
        return osuApiStatus switch
        {
            -2 => RankedStatus.Pending, // graveyard
            -1 => RankedStatus.Pending, // wip
            0 => RankedStatus.Pending,
            1 => RankedStatus.Ranked,
            2 => RankedStatus.Approved,
            3 => RankedStatus.Qualified,
            4 => RankedStatus.Loved,
            _ => RankedStatus.UpdateAvailable
        };
    }

    /// <summary>Ported from RankedStatus.from_osudirect (defaultdict falls back to UpdateAvailable).</summary>
    public static RankedStatus FromOsuDirect(int osuDirectStatus)
    {
        return osuDirectStatus switch
        {
            0 => RankedStatus.Ranked,
            2 => RankedStatus.Pending,
            3 => RankedStatus.Qualified,
            5 => RankedStatus.Pending, // graveyard
            7 => RankedStatus.Ranked, // played before
            8 => RankedStatus.Loved,
            _ => RankedStatus.UpdateAvailable
        };
    }
}