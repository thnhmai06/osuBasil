using System.Text;

namespace Bancho.Domain;

/// <summary>Ported from app/api/domains/cho.py's parse_login_data.</summary>
public static class LoginDataParser
{
    public static LoginData Parse(byte[] data)
    {
        var decoded = Encoding.UTF8.GetString(data).TrimEnd('\n');

        var top = decoded.Split('\n', 3);
        var username = top[0];
        var passwordMd5 = top[1];
        var remainder = top[2];

        var fields = remainder.Split('|', 5);
        var osuVersion = fields[0];
        var utcOffset = int.Parse(fields[1]);
        var displayCity = fields[2] == "1";
        var clientHashes = fields[3];
        var pmPrivate = fields[4] == "1";

        // client_hashes has a trailing ':' delimiter — strip it before splitting.
        var hashParts = clientHashes[..^1].Split(':', 5);

        return new LoginData(
            Username: username,
            PasswordMd5: Encoding.UTF8.GetBytes(passwordMd5),
            OsuVersion: osuVersion,
            UtcOffset: utcOffset,
            DisplayCity: displayCity,
            PmPrivate: pmPrivate,
            OsuPathMd5: hashParts[0],
            AdaptersString: hashParts[1],
            AdaptersMd5: hashParts[2],
            UninstallMd5: hashParts[3],
            DiskSignatureMd5: hashParts[4]);
    }
}
