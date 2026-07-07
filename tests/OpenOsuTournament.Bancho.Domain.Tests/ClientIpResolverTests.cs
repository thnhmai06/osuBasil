using System.Net;
using OpenOsuTournament.Bancho.Domain.Login;

namespace OpenOsuTournament.Bancho.Domain.Tests;

/// <summary>Ported from app/state/services.py's IPResolver.get_ip.</summary>
public class ClientIpResolverTests
{
    [Fact]
    public void Resolve_CfConnectingIpHeader_TakesPriority()
    {
        var headers = new Dictionary<string, string> { ["CF-Connecting-IP"] = "1.2.3.4", ["X-Real-IP"] = "9.9.9.9" };

        Assert.Equal(IPAddress.Parse("1.2.3.4"), ClientIpResolver.Resolve(headers));
    }

    [Fact]
    public void Resolve_NoCloudflare_SingleForwardedFor_UsesXRealIp()
    {
        var headers = new Dictionary<string, string> { ["X-Forwarded-For"] = "5.6.7.8", ["X-Real-IP"] = "9.9.9.9" };

        Assert.Equal(IPAddress.Parse("9.9.9.9"), ClientIpResolver.Resolve(headers));
    }

    [Fact]
    public void Resolve_NoCloudflare_MultipleForwardedFor_UsesFirstForwardedForEntry()
    {
        var headers = new Dictionary<string, string> { ["X-Forwarded-For"] = "5.6.7.8, 10.0.0.1" };

        Assert.Equal(IPAddress.Parse("5.6.7.8"), ClientIpResolver.Resolve(headers));
    }
}