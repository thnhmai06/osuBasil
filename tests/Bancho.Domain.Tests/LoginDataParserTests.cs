using System.Text;

namespace Bancho.Domain.Tests;

/// <summary>Ported from app/api/domains/cho.py's parse_login_data.</summary>
public class LoginDataParserTests
{
    private static byte[] Body(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Parse_TypicalLoginBody_ParsesAllFields()
    {
        var body = Body(
            "cmyui\n" +
            "5f4dcc3b5aa765d61d8327deb882cf99\n" +
            "b20231231.1cuttingedge|-5|1|" +
            "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4:runningunderwine:00000000000000000000000000000000:" +
            "11111111111111111111111111111111:22222222222222222222222222222222:|0\n");

        var login = LoginDataParser.Parse(body);

        Assert.Equal("cmyui", login.Username);
        Assert.Equal("5f4dcc3b5aa765d61d8327deb882cf99", Encoding.UTF8.GetString(login.PasswordMd5));
        Assert.Equal("b20231231.1cuttingedge", login.OsuVersion);
        Assert.Equal(-5, login.UtcOffset);
        Assert.True(login.DisplayCity);
        Assert.False(login.PmPrivate);
        Assert.Equal("a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4", login.OsuPathMd5);
        Assert.Equal("runningunderwine", login.AdaptersString);
        Assert.Equal("00000000000000000000000000000000", login.AdaptersMd5);
        Assert.Equal("11111111111111111111111111111111", login.UninstallMd5);
        Assert.Equal("22222222222222222222222222222222", login.DiskSignatureMd5);
    }

    [Fact]
    public void Parse_TrailingNewline_IsStripped()
    {
        var body = Body(
            "user\npass\nb20231231|0|0|a:adapters.:b:c:d:|1\n\n");

        var login = LoginDataParser.Parse(body);

        Assert.Equal("user", login.Username);
    }
}
