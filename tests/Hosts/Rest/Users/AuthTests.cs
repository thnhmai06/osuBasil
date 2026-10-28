using System.Net;

namespace Basil.Host.Rest.Tests.Users;

/// <summary>Sign-in, sign-out and the public/token rule (ADR-009).</summary>
[Collection("REST host")]
public sealed class AuthTests(RestHostFixture fixture)
{
	[Fact]
	public async Task Login_WithTheRightPassword_Answers201WithATokenThatNamesTheSession()
	{
		var user = await fixture.Host.CreateUserAsync("AuthOk");
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);

		var (status, envelope) = await client.CallAsync(HttpMethod.Post, "/v1/auth/login",
			new { username = "AuthOk", password = "password" });

		Assert.Equal(201, status);
		Assert.True((bool)envelope!["success"]!);
		Assert.Equal(user.Id, (int)envelope["data"]!["userId"]!);
		client.Bearer = (string)envelope["data"]!["token"]!;
		var (sessionStatus, session) = await client.CallAsync(HttpMethod.Get, "/v1/auth/session");
		Assert.Equal(200, sessionStatus);
		Assert.Equal(user.Id, (int)session!["data"]!["userId"]!);
	}

	[Fact]
	public async Task Login_WithAWrongPassword_Answers401()
	{
		await fixture.Host.CreateUserAsync("AuthBad");
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);

		var (status, envelope) = await client.CallAsync(HttpMethod.Post, "/v1/auth/login",
			new { username = "AuthBad", password = "nope" });

		Assert.Equal(401, status);
		Assert.False((bool)envelope!["success"]!);
		Assert.Equal("invalidCredentials", (string)envelope["errors"]!["code"]!);
	}

	[Fact]
	public async Task TokenRoute_WithoutAToken_Answers401InTheEnvelope()
	{
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);

		var (status, envelope) = await client.CallAsync(HttpMethod.Get, "/v1/auth/session");

		Assert.Equal(401, status);
		Assert.Equal(401, (int)envelope!["code"]!);
	}

	[Fact]
	public async Task TokenRoute_WithAGarbageToken_Answers401()
	{
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain, "garbage");

		var (status, _) = await client.CallAsync(HttpMethod.Get, "/v1/auth/session");

		Assert.Equal(401, status);
	}

	[Fact]
	public async Task Logout_RevokesTheToken()
	{
		var (client, _) = await fixture.SignInAsync("AuthOut");
		using (client)
		{
			var (status, _) = await client.CallAsync(HttpMethod.Post, "/v1/auth/logout");
			var (after, _) = await client.CallAsync(HttpMethod.Get, "/v1/auth/session");

			Assert.Equal(200, status);
			Assert.Equal(401, after);
		}
	}

	[Fact]
	public async Task PublicRoute_WithoutAToken_Answers200()
	{
		var user = await fixture.Host.CreateUserAsync("AuthPublic");
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);

		var (status, envelope) = await client.CallAsync(HttpMethod.Get, $"/v1/users/{user.Id}");

		Assert.Equal(200, status);
		Assert.Equal("AuthPublic", (string)envelope!["data"]!["name"]!);
	}

	[Fact]
	public async Task UnknownRoute_Answers404InTheEnvelope()
	{
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);

		var (status, envelope) = await client.CallAsync(HttpMethod.Get, "/v1/nothing");

		Assert.Equal(404, status);
		Assert.False((bool)envelope!["success"]!);
	}

	[Fact]
	public async Task Health_IsPublic()
	{
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);

		using var response = await client.GetAsync("/health");

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
	}
}
