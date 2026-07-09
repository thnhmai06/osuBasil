using Basil.Application.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.Application.Tests.Configuration;

/// <summary>Verifies every configuration section Basil actually reads binds correctly through the standard IOptions&lt;T&gt; pipeline.</summary>
public class OptionsBindingTests
{
    private static T BindOptions<T>(string sectionName, Dictionary<string, string?> values)
        where T : class
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var services = new ServiceCollection();
        services.Configure<T>(configuration.GetSection(sectionName));

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<T>>().Value;
    }

    [Fact]
    public void DatabaseOptions_Path_DefaultsToBasilDb()
    {
        var options = BindOptions<DatabaseOptions>(DatabaseOptions.SectionName, new Dictionary<string, string?>());

        Assert.Equal("basil.db", options.Path);
    }

    [Fact]
    public void DatabaseOptions_Binds_Path()
    {
        var options = BindOptions<DatabaseOptions>(DatabaseOptions.SectionName, new Dictionary<string, string?>
        {
            [$"{DatabaseOptions.SectionName}:Path"] = "/srv/basil.db"
        });

        Assert.Equal("/srv/basil.db", options.Path);
    }

    [Fact]
    public void MirrorOptions_Binds_DownloadEndpoint()
    {
        var options = BindOptions<MirrorOptions>(MirrorOptions.SectionName, new Dictionary<string, string?>
        {
            [$"{MirrorOptions.SectionName}:DownloadEndpoint"] = "https://catboy.best/d"
        });

        Assert.Equal("https://catboy.best/d", options.DownloadEndpoint);
    }

    [Fact]
    public void MirrorOptions_DownloadEndpoint_IsNullByDefault()
    {
        var options = BindOptions<MirrorOptions>(MirrorOptions.SectionName, new Dictionary<string, string?>());

        Assert.Null(options.DownloadEndpoint);
    }

    [Fact]
    public void ServerBehaviorOptions_Binds_AllFields()
    {
        var options = BindOptions<ServerBehaviorOptions>(ServerBehaviorOptions.SectionName,
            new Dictionary<string, string?>
            {
                [$"{ServerBehaviorOptions.SectionName}:Domain"] = "akatsuki.gg",
                [$"{ServerBehaviorOptions.SectionName}:MenuIconPath"] = "icon.png",
                [$"{ServerBehaviorOptions.SectionName}:MenuOnclickUrl"] = "https://a.example"
            });

        Assert.Equal("akatsuki.gg", options.Domain);
        Assert.Equal("icon.png", options.MenuIconPath);
        Assert.Equal("https://a.example", options.MenuOnclickUrl);
    }

    [Fact]
    public void BotOptions_Binds_NameAndCommandPrefix()
    {
        var options = BindOptions<BotOptions>(BotOptions.SectionName, new Dictionary<string, string?>
        {
            [$"{BotOptions.SectionName}:Name"] = "TourneyBot",
            [$"{BotOptions.SectionName}:CommandPrefix"] = "!"
        });

        Assert.Equal("TourneyBot", options.Name);
        Assert.Equal("!", options.CommandPrefix);
    }
}
