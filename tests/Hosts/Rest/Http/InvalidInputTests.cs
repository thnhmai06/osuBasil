using System.Text.Json;
using Basil.Host.Shared;
using Basil.Host.Shared.Json;

namespace Basil.Host.Rest.Tests.Http;

/// <summary>Input the API cannot read is answered with the error envelope, never with an exception or an empty body.</summary>
[Collection("REST host")]
public sealed class InvalidInputTests(RestHostFixture fixture)
{
	private RestClient Anonymous()
	{
		return new RestClient(fixture.Host.Url, fixture.Host.Domain);
	}

	[Theory]
	[InlineData("/v1/scores?limit=abc")]
	[InlineData("/v1/scores?offset=99999999999")]
	[InlineData("/v1/scores?user=-x")]
	public async Task AQueryValueThatIsNotANumber_Answers400WithTheInvalidCode(string path)
	{
		using var client = Anonymous();

		var (status, envelope) = await client.CallAsync(HttpMethod.Get, path);

		Assert.Equal(400, status);
		Assert.False((bool)envelope!["success"]!);
		Assert.Equal("invalid", (string)envelope["errors"]!["code"]!);
	}

	[Fact]
	public async Task APathNumberThatDoesNotFitAnInt_Answers422WithTheInvalidCode()
	{
		using var client = Anonymous();

		var (status, envelope) = await client.CallAsync(HttpMethod.Get, "/v1/scores/99999999999");

		Assert.Equal(422, status);
		Assert.Equal("invalid", (string)envelope!["errors"]!["code"]!);
	}

	[Fact]
	public async Task ABodyThatIsNotTheExpectedJson_Answers400WithTheInvalidCode()
	{
		var (client, _) = await fixture.SignInAsync("InvalidBody");
		using (client)
		{
			var (status, envelope) = await client.CallAsync(HttpMethod.Post, "/v1/rooms", "not an object");

			Assert.Equal(400, status);
			Assert.Equal("invalid", (string)envelope!["errors"]!["code"]!);
		}
	}

	[Fact]
	public async Task APageBeyondTheLastItem_IsAnEmptyPage()
	{
		using var client = Anonymous();

		var (status, envelope) = await client.CallAsync(HttpMethod.Get, "/v1/scores?offset=100000");

		Assert.Equal(200, status);
		Assert.Empty(envelope!["data"]!.AsArray());
	}

	[Fact]
	public async Task TheServerHeader_IsNotSent()
	{
		using var client = Anonymous();

		using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

		Assert.False(response.Headers.Contains("Server"));
	}

	[Fact]
	public void PlusAndNonAsciiLetters_AreNotEscapedInJson()
	{
		Assert.Equal("\"a+b é\"", JsonSerializer.Serialize("a+b é", ApiJson.Options));
	}
}