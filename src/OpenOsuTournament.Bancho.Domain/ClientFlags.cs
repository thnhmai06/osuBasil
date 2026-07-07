namespace OpenOsuTournament.Bancho.Domain;

/// <summary>
///     Ported from app/constants/clientflags.py's ClientFlags. osu!'s anticheat flags (&lt;= 2016) sent
///     alongside a score submission — informational only, many are outdated/known to false-positive.
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
    RawKeyboardDiscrepancy = 1 << 13
}

/// <summary>
///     Ported from app/constants/clientflags.py's LastFMFlags (osu! anticheat 2019), read by
///     /web/lastfm.php via ClientIntegrityService — despite the similar name, this is a distinct bit
///     range from <see cref="ClientFlags" />, sent on a separate endpoint outside score submission.
/// </summary>
[Flags]
public enum LastFMFlags
{
    RunWithLdFlag = 1 << 14,
    ConsoleOpen = 1 << 15,
    ExtraThreads = 1 << 16,
    HqAssembly = 1 << 17,
    HqFile = 1 << 18,
    RegistryEdits = 1 << 19,
    Sdl2Library = 1 << 20,
    OpenSslLibrary = 1 << 21,
    AqnMenuSample = 1 << 22
}