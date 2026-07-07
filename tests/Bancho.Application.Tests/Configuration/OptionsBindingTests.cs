using Bancho.Application.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bancho.Application.Tests.Configuration;

/// <summary>
///     Verifies every configuration section (ported from app/settings.py's ~50 env vars)
///     binds correctly through the standard IOptions&lt;T&gt; pipeline.
/// </summary>
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
    public void AppOptions_Binds_HostAndPort()
    {
        var options = BindOptions<AppOptions>(AppOptions.SectionName, new Dictionary<string, string?>
        {
            [$"{AppOptions.SectionName}:Host"] = "127.0.0.1",
            [$"{AppOptions.SectionName}:Port"] = "8080"
        });

        Assert.Equal("127.0.0.1", options.Host);
        Assert.Equal(8080, options.Port);
    }

    [Fact]
    public void DatabaseOptions_Binds_ConnectionFields()
    {
        var options = BindOptions<DatabaseOptions>(DatabaseOptions.SectionName, new Dictionary<string, string?>
        {
            [$"{DatabaseOptions.SectionName}:Host"] = "db.local",
            [$"{DatabaseOptions.SectionName}:Port"] = "3306",
            [$"{DatabaseOptions.SectionName}:User"] = "bancho",
            [$"{DatabaseOptions.SectionName}:Password"] = "p@ss:w/ord",
            [$"{DatabaseOptions.SectionName}:Name"] = "bancho"
        });

        Assert.Equal("db.local", options.Host);
        Assert.Equal(3306, options.Port);
        Assert.Equal("bancho", options.User);
        Assert.Equal("p@ss:w/ord", options.Password);
        Assert.Equal("bancho", options.Name);
    }

    [Fact]
    public void RedisOptions_Binds_ConnectionFields()
    {
        var options = BindOptions<RedisOptions>(RedisOptions.SectionName, new Dictionary<string, string?>
        {
            [$"{RedisOptions.SectionName}:Host"] = "redis.local",
            [$"{RedisOptions.SectionName}:Port"] = "6379",
            [$"{RedisOptions.SectionName}:User"] = "default",
            [$"{RedisOptions.SectionName}:Password"] = "hunter2",
            [$"{RedisOptions.SectionName}:Database"] = "0"
        });

        Assert.Equal("redis.local", options.Host);
        Assert.Equal(6379, options.Port);
        Assert.Equal("default", options.User);
        Assert.Equal("hunter2", options.Password);
        Assert.Equal(0, options.Database);
    }

    [Fact]
    public void OsuApiOptions_ApiKey_IsNullByDefault()
    {
        var options = BindOptions<OsuApiOptions>(OsuApiOptions.SectionName, new Dictionary<string, string?>());

        Assert.Null(options.ApiKey);
    }

    [Fact]
    public void OsuApiOptions_ApiKey_BindsWhenPresent()
    {
        var options = BindOptions<OsuApiOptions>(OsuApiOptions.SectionName, new Dictionary<string, string?>
        {
            [$"{OsuApiOptions.SectionName}:ApiKey"] = "abc123"
        });

        Assert.Equal("abc123", options.ApiKey);
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
    public void StorageOptions_ReplaysPath_DefaultsToDotDataOsr()
    {
        var options = BindOptions<StorageOptions>(StorageOptions.SectionName, new Dictionary<string, string?>());

        Assert.Equal(Path.Combine(".data", "osr"), options.ReplaysPath);
    }

    [Fact]
    public void StorageOptions_Binds_ReplaysPath()
    {
        var options = BindOptions<StorageOptions>(StorageOptions.SectionName, new Dictionary<string, string?>
        {
            [$"{StorageOptions.SectionName}:ReplaysPath"] = "/srv/replays"
        });

        Assert.Equal("/srv/replays", options.ReplaysPath);
    }

    [Fact]
    public void ServerBehaviorOptions_Binds_AllFields()
    {
        var options = BindOptions<ServerBehaviorOptions>(ServerBehaviorOptions.SectionName,
            new Dictionary<string, string?>
            {
                [$"{ServerBehaviorOptions.SectionName}:Domain"] = "akatsuki.gg",
                [$"{ServerBehaviorOptions.SectionName}:CommandPrefix"] = "!",
                [$"{ServerBehaviorOptions.SectionName}:SeasonalBackgrounds:0"] = "https://a.example/1.png",
                [$"{ServerBehaviorOptions.SectionName}:SeasonalBackgrounds:1"] = "https://a.example/2.png",
                [$"{ServerBehaviorOptions.SectionName}:MenuIconUrl"] = "https://a.example/icon.png",
                [$"{ServerBehaviorOptions.SectionName}:MenuOnclickUrl"] = "https://a.example",
                [$"{ServerBehaviorOptions.SectionName}:Debug"] = "true",
                [$"{ServerBehaviorOptions.SectionName}:RedirectOsuUrls"] = "false",
                [$"{ServerBehaviorOptions.SectionName}:DeveloperMode"] = "false",
                [$"{ServerBehaviorOptions.SectionName}:LogWithColors"] = "true",
                [$"{ServerBehaviorOptions.SectionName}:AutomaticallyReportProblems"] = "false"
            });

        Assert.Equal("akatsuki.gg", options.Domain);
        Assert.Equal("!", options.CommandPrefix);
        Assert.Equal(["https://a.example/1.png", "https://a.example/2.png"], options.SeasonalBackgrounds);
        Assert.Equal("https://a.example/icon.png", options.MenuIconUrl);
        Assert.Equal("https://a.example", options.MenuOnclickUrl);
        Assert.True(options.Debug);
        Assert.False(options.RedirectOsuUrls);
        Assert.False(options.DeveloperMode);
        Assert.True(options.LogWithColors);
        Assert.False(options.AutomaticallyReportProblems);
    }

    [Fact]
    public void DatadogOptions_Binds_Keys()
    {
        var options = BindOptions<DatadogOptions>(DatadogOptions.SectionName, new Dictionary<string, string?>
        {
            [$"{DatadogOptions.SectionName}:ApiKey"] = "dd-api",
            [$"{DatadogOptions.SectionName}:AppKey"] = "dd-app"
        });

        Assert.Equal("dd-api", options.ApiKey);
        Assert.Equal("dd-app", options.AppKey);
    }

    [Fact]
    public void DatadogOptions_IsEnabled_FalseWhenEitherKeyIsEmpty()
    {
        var options = BindOptions<DatadogOptions>(DatadogOptions.SectionName, new Dictionary<string, string?>
        {
            [$"{DatadogOptions.SectionName}:ApiKey"] = "",
            [$"{DatadogOptions.SectionName}:AppKey"] = "dd-app"
        });

        Assert.False(options.IsEnabled);
    }

    [Fact]
    public void PerformanceOptions_Binds_CachedAccuracies()
    {
        var options = BindOptions<PerformanceOptions>(PerformanceOptions.SectionName, new Dictionary<string, string?>
        {
            [$"{PerformanceOptions.SectionName}:CachedAccuracies:0"] = "95",
            [$"{PerformanceOptions.SectionName}:CachedAccuracies:1"] = "98",
            [$"{PerformanceOptions.SectionName}:CachedAccuracies:2"] = "99",
            [$"{PerformanceOptions.SectionName}:CachedAccuracies:3"] = "100"
        });

        Assert.Equal([95, 98, 99, 100], options.CachedAccuracies);
    }

    [Fact]
    public void RegistrationOptions_Binds_AllFields()
    {
        var options = BindOptions<RegistrationOptions>(RegistrationOptions.SectionName, new Dictionary<string, string?>
        {
            [$"{RegistrationOptions.SectionName}:DisallowedNames:0"] = "admin",
            [$"{RegistrationOptions.SectionName}:DisallowedNames:1"] = "peppy",
            [$"{RegistrationOptions.SectionName}:DisallowedPasswords:0"] = "password",
            [$"{RegistrationOptions.SectionName}:DisallowIngameRegistration"] = "false"
        });

        Assert.Equal(["admin", "peppy"], options.DisallowedNames);
        Assert.Equal(["password"], options.DisallowedPasswords);
        Assert.False(options.DisallowIngameRegistration);
    }

    [Fact]
    public void CaptchaOptions_ProviderAndSecret_AreNullByDefault()
    {
        var options = BindOptions<CaptchaOptions>(CaptchaOptions.SectionName, new Dictionary<string, string?>());

        Assert.Null(options.Provider);
        Assert.Null(options.Secret);
    }

    [Fact]
    public void CaptchaOptions_Binds_ProviderAndSecret()
    {
        var options = BindOptions<CaptchaOptions>(CaptchaOptions.SectionName, new Dictionary<string, string?>
        {
            [$"{CaptchaOptions.SectionName}:Provider"] = "turnstile",
            [$"{CaptchaOptions.SectionName}:Secret"] = "secret-value"
        });

        Assert.Equal("turnstile", options.Provider);
        Assert.Equal("secret-value", options.Secret);
    }

    [Fact]
    public void WebSessionOptions_Binds_CookieSecure()
    {
        var options = BindOptions<WebSessionOptions>(WebSessionOptions.SectionName, new Dictionary<string, string?>
        {
            [$"{WebSessionOptions.SectionName}:CookieSecure"] = "true"
        });

        Assert.True(options.CookieSecure);
    }

    [Fact]
    public void DiscordOptions_Binds_WebhookAndInvite()
    {
        var options = BindOptions<DiscordOptions>(DiscordOptions.SectionName, new Dictionary<string, string?>
        {
            [$"{DiscordOptions.SectionName}:AuditLogWebhookUrl"] = "https://discord.com/api/webhooks/x",
            [$"{DiscordOptions.SectionName}:InviteUrl"] = "https://discord.gg/x"
        });

        Assert.Equal("https://discord.com/api/webhooks/x", options.AuditLogWebhookUrl);
        Assert.Equal("https://discord.gg/x", options.InviteUrl);
    }
}