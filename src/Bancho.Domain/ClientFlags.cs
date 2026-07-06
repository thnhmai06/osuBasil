namespace Bancho.Domain;

/// <summary>
/// Ported from app/constants/clientflags.py's ClientFlags. osu!'s anticheat flags (&lt;= 2016) sent
/// alongside a score submission — informational only, many are outdated/known to false-positive.
/// LastFMFlags (2019) are intentionally not ported: they were only ever sent to official
/// osu!bancho and bancho.py never read them either.
/// </summary>
[Flags]
public enum ClientFlags
{
    Clean = 0,
    SpeedHackDetected = 1 << 1,
    IncorrectModValue = 1 << 2,
    MultipleOsuClients = 1 << 3,
    ChecksumFailure = 1 << 4,
    FlashlightChecksumIncorrect = 1 << 5,
    OsuExecutableChecksum = 1 << 6,
    MissingProcessesInList = 1 << 7,
    FlashlightImageHack = 1 << 8,
    SpinnerHack = 1 << 9,
    TransparentWindow = 1 << 10,
    FastPress = 1 << 11,
    RawMouseDiscrepancy = 1 << 12,
    RawKeyboardDiscrepancy = 1 << 13,
}
