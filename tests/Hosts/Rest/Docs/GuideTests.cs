namespace Basil.Host.Rest.Tests.Docs;

/// <summary>The guides the parts of the server write about themselves are served by the API host.</summary>
[Collection("REST host")]
public sealed class GuideTests(RestHostFixture fixture)
{
	private async Task<(int Status, string? Type, string Body)> GetAsync(string path)
	{
		using var client = new RestClient(fixture.Host.Url, fixture.Host.Domain);
		using var response = await client.GetAsync(path);
		return ((int)response.StatusCode, response.Content.Headers.ContentType?.MediaType,
			await response.Content.ReadAsStringAsync());
	}

	[Theory]
	[InlineData("bot", "!mp mods")]
	[InlineData("irc", "AUTHENTICATE")]
	public async Task KnownGuide_MarkdownAndPage(string slug, string expected)
	{
		var markdown = await GetAsync($"/guides/{slug}.md");
		var page = await GetAsync($"/guides/{slug}");

		Assert.Equal(200, markdown.Status);
		Assert.Equal("text/markdown", markdown.Type);
		Assert.Contains(expected, markdown.Body);
		Assert.Equal(200, page.Status);
		Assert.Equal("text/html", page.Type);
		Assert.Contains("<table>", page.Body);
		Assert.Contains(expected, page.Body);
	}

	[Fact]
	public async Task GuideIndex_ListsEveryGuide()
	{
		var index = await GetAsync("/guides");

		Assert.Equal(200, index.Status);
		Assert.Contains("/guides/bot", index.Body);
		Assert.Contains("/guides/irc", index.Body);
	}

	[Fact]
	public async Task UnknownGuide_NotFound()
	{
		Assert.Equal(404, (await GetAsync("/guides/nothing")).Status);
		Assert.Equal(404, (await GetAsync("/guides/nothing.md")).Status);
	}
}