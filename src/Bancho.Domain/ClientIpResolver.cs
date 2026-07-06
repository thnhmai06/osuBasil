using System.Net;

namespace Bancho.Domain;

/// <summary>Ported from app/state/services.py's IPResolver.get_ip.</summary>
public static class ClientIpResolver
{
    public static IPAddress Resolve(IReadOnlyDictionary<string, string> headers)
    {
        if (headers.TryGetValue("CF-Connecting-IP", out var cfIp))
        {
            return IPAddress.Parse(cfIp);
        }

        var forwards = headers["X-Forwarded-For"].Split(',');
        var ipStr = forwards.Length != 1 ? forwards[0].Trim() : headers["X-Real-IP"];
        return IPAddress.Parse(ipStr);
    }
}
