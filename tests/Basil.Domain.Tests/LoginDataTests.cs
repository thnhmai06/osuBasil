using System.Text;
using Basil.Domain.Login;

namespace Basil.Domain.Tests;

/// <summary>Ported from app/api/domains/cho.py's parse_login_data.</summary>
public class LoginDataTests
{
    private static byte[] Body(string s)
    {
        return Encoding.UTF8.GetBytes(s);
    }

    [Fact]
    public void From_TypicalLoginBody_ParsesAllFields()
    {
        var body = Body(
            "cmyui\n" +
            "5f4dcc3b5aa765d61d8327deb882cf99\n" +
            "b20231231.1cuttingedge|-5|1|" +
            "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4:runningunderwine:00000000000000000000000000000000:" +
            "11111111111111111111111111111111:22222222222222222222222222222222:|0\n");

        var login = LoginData.From(body);

        Assert.Equal("cmyui", login.Username);
        Assert.Equal("5f4dcc3b5aa765d61d8327deb882cf99", login.PasswordMd5);
        Assert.Equal(new DateOnly(2023, 12, 31), login.OsuVersion.Date);
        Assert.Equal(1, login.OsuVersion.Revision);
        Assert.Equal(OsuStream.CuttingEdge, login.OsuVersion.Stream);
        Assert.Equal(-5, login.UtcOffset);
        Assert.True(login.DisplayCity);
        Assert.False(login.PmPrivate);
        Assert.Equal("a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4", login.ClientDetails.OsuPathMd5);
        Assert.Equal(["runningunderwine"], login.ClientDetails.Adapters);
        Assert.Equal("00000000000000000000000000000000", login.ClientDetails.AdaptersMd5);
        Assert.Equal("11111111111111111111111111111111", login.ClientDetails.UninstallMd5);
        Assert.Equal("22222222222222222222222222222222", login.ClientDetails.DiskSignatureMd5);
    }

    [Fact]
    public void From_TrailingNewline_IsStripped()
    {
        var body = Body(
            "user\npass\nb20231231|0|0|a:adapters.:b:c:d:|1\n\n");

        var login = LoginData.From(body);

        Assert.Equal("user", login.Username);
    }
}
