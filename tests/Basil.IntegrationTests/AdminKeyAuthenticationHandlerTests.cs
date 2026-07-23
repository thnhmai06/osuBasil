using System.Text.Encodings.Web;
using Basil.Application.Configuration;
using Basil.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Basil.IntegrationTests;

/// <summary>
///     Exercises <see cref="AdminKeyAuthenticationHandler" /> directly (no HTTP host needed — just an
///     <see cref="HttpContext" />), confirming the "soft" contract: a missing/wrong key never fails the
///     request outright (<see cref="AuthenticateResult.NoResult" />, not <c>Fail</c>) — only a
///     correct key produces a <see cref="AdminKeyDefaults.Role" />-carrying principal.
/// </summary>
public class AdminKeyAuthenticationHandlerTests
{
    private const string ConfiguredKey = "s3cr3t";

    private static async Task<AuthenticateResult> AuthenticateAsync(string? providedKey)
    {
        var handler = new AdminKeyAuthenticationHandler(
            new StaticOptionsMonitor(),
            LoggerFactory.Create(_ => { }),
            UrlEncoder.Default,
            Options.Create(new ServerOptions
            {
                Domain = "test.local",
                MenuIconPath = "icon.png",
                MenuOnclickUrl = "https://example.test",
                AdminKey = ConfiguredKey
            }));

        var context = new DefaultHttpContext();
        if (providedKey is not null)
            context.Request.Headers["X-Admin-Key"] = providedKey;

        var scheme = new AuthenticationScheme(AdminKeyDefaults.Scheme, null, typeof(AdminKeyAuthenticationHandler));
        await handler.InitializeAsync(scheme, context);
        return await handler.AuthenticateAsync();
    }

    [Fact]
    public async Task AuthenticateAsync_CorrectKey_SucceedsWithAdminRole()
    {
        var result = await AuthenticateAsync(ConfiguredKey);

        Assert.True(result.Succeeded);
        Assert.True(result.Principal!.IsInRole(AdminKeyDefaults.Role));
    }

    [Fact]
    public async Task AuthenticateAsync_MissingKey_NoResultNotFail()
    {
        var result = await AuthenticateAsync(null);

        Assert.False(result.Succeeded);
        Assert.Null(result.Failure);
    }

    [Fact]
    public async Task AuthenticateAsync_WrongKey_NoResultNotFail()
    {
        var result = await AuthenticateAsync("wrong-key");

        Assert.False(result.Succeeded);
        Assert.Null(result.Failure);
    }

    [Fact]
    public async Task AuthenticateAsync_WrongKey_BehavesIdenticallyToMissingKey()
    {
        var missing = await AuthenticateAsync(null);
        var wrong = await AuthenticateAsync("wrong-key");

        Assert.Equal(missing.Succeeded, wrong.Succeeded);
        Assert.Equal(missing.Failure, wrong.Failure);
    }

    /// <summary>Fixed, always-default options — this handler never customizes <see cref="AuthenticationSchemeOptions" />.</summary>
    private sealed class StaticOptionsMonitor : IOptionsMonitor<AuthenticationSchemeOptions>
    {
        public AuthenticationSchemeOptions CurrentValue { get; } = new();

        public AuthenticationSchemeOptions Get(string? name)
        {
            return CurrentValue;
        }

        public IDisposable? OnChange(Action<AuthenticationSchemeOptions, string?> listener)
        {
            return null;
        }
    }
}
