namespace OpenOsuTournament.Bancho.Domain.Login;

/// <summary>Ported from app/api/domains/cho.py's LoginData (TypedDict).</summary>
public sealed record LoginData(
    string Username,
    byte[] PasswordMd5,
    string OsuVersion,
    int UtcOffset,
    bool DisplayCity,
    bool PmPrivate,
    string OsuPathMd5,
    string AdaptersString,
    string AdaptersMd5,
    string UninstallMd5,
    string DiskSignatureMd5);