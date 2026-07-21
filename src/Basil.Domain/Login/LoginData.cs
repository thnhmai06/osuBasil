using System.Text;

namespace Basil.Domain.Login;

/// <summary>Ported from app/api/domains/cho.py's LoginData (TypedDict).</summary>
public sealed record LoginData(
    string Username,
    string PasswordMd5,
    OsuVersion OsuVersion,
    int UtcOffset,
    bool DisplayCity,
    bool PmPrivate,
    ClientDetails ClientDetails)
{
    public static LoginData From(byte[] data)
    {
        var decoded = Encoding.UTF8.GetString(data).TrimEnd('\n');

        var top = decoded.Split('\n', 3);
        var username = top[0];
        var passwordMd5 = top[1];
        var remainder = top[2];

        var fields = remainder.Split('|', 5);
        var osuVersion = OsuVersion.From(fields[0]);
        var utcOffset = int.Parse(fields[1]);
        var displayCity = fields[2] == "1";
        var clientHashes = fields[3];
        var pmPrivate = fields[4] == "1";
        var clientDetails = ClientDetails.From(clientHashes);

        return new LoginData(username, passwordMd5, osuVersion, utcOffset, displayCity, pmPrivate, clientDetails);
    }
}