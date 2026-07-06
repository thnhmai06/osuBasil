using System.Globalization;
using System.Net;
using Bancho.Application.Abstractions;
using Bancho.Domain;

namespace Bancho.Infrastructure.External;

/// <inheritdoc cref="IGeolocationProvider" />
public sealed class IpApiGeolocationProvider(HttpClient httpClient) : IGeolocationProvider
{
    private const string Fields = "status,message,countryCode,lat,lon";

    public async Task<Geolocation?> FetchByIpAsync(IPAddress ip, CancellationToken cancellationToken = default)
    {
        // matches bancho.py: a private/loopback ip is omitted from the URL, letting ip-api.com
        // detect the caller's own public IP instead (useful for local dev behind NAT).
        var url = IsPrivate(ip)
            ? $"http://ip-api.com/line/?fields={Fields}"
            : $"http://ip-api.com/line/{ip}?fields={Fields}";

        var response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var lines = (await response.Content.ReadAsStringAsync(cancellationToken)).Split('\n');
        if (lines.Length == 0 || lines[0] != "success")
        {
            return null;
        }

        var countryAcronym = lines[1].ToLowerInvariant();
        if (!CountryCodes.ByAcronym.TryGetValue(countryAcronym, out var countryNumeric))
        {
            return null;
        }

        return new Geolocation(
            double.Parse(lines[2], CultureInfo.InvariantCulture),
            double.Parse(lines[3], CultureInfo.InvariantCulture),
            countryAcronym,
            countryNumeric);
    }

    private static bool IsPrivate(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        var bytes = ip.GetAddressBytes();
        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || bytes.Length != 4)
        {
            return false;
        }

        return bytes[0] switch
        {
            10 => true,
            172 => bytes[1] is >= 16 and <= 31,
            192 => bytes[1] == 168,
            _ => false,
        };
    }
}
