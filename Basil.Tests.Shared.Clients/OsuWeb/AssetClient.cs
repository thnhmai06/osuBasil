namespace Basil.Tests.Shared.Clients.OsuWeb;

/// <summary>Reads the pictures and audio the image hosts serve; every call counts the bytes of the answer instead of keeping them.</summary>
/// <remarks>These hosts need no sign-in.</remarks>
public sealed class AssetClient(Endpoint endpoint, HttpClient http)
{
	/// <summary>Reads the avatar of a user from the <c>a.</c> host.</summary>
	public Task<Reply<long>> GetAvatarAsync(int userId, CancellationToken ct)
	{
		return DrainAsync("a", $"/{userId}", ct);
	}

	/// <summary>Reads a cover of a beatmapset from the <c>assets.</c> host.</summary>
	/// <param name="setId">The beatmapset id.</param>
	/// <param name="variant">One of <c>cover</c>, <c>card</c>, <c>list</c> or <c>slimcover</c>.</param>
	public Task<Reply<long>> GetCoverAsync(int setId, string variant, CancellationToken ct)
	{
		return DrainAsync("assets", $"/beatmapsets/{setId}/covers/{variant}.jpg", ct);
	}

	/// <summary>Reads the thumbnail of a beatmapset from the <c>b.</c> host.</summary>
	/// <param name="setId">The beatmapset id.</param>
	/// <param name="large">Whether to read the large thumbnail.</param>
	public Task<Reply<long>> GetThumbnailAsync(int setId, bool large, CancellationToken ct)
	{
		return DrainAsync("b", $"/thumb/{setId}{(large ? "l" : string.Empty)}.jpg", ct);
	}

	/// <summary>Reads the audio preview of a beatmapset from the <c>b.</c> host.</summary>
	public Task<Reply<long>> GetPreviewAsync(int setId, CancellationToken ct)
	{
		return DrainAsync("b", $"/preview/{setId}.mp3", ct);
	}

	/// <summary>Reads a stored screenshot from the <c>osu.</c> host.</summary>
	/// <param name="fileName">The name the upload answered with, such as <c>0c2f.png</c>.</param>
	public Task<Reply<long>> GetScreenshotAsync(string fileName, CancellationToken ct)
	{
		return DrainAsync("osu", $"/ss/{fileName}", ct);
	}

	/// <summary>Reads a picture of the menu (a seasonal background or a banner) from the <c>assets.</c> host.</summary>
	/// <param name="path">The path under <c>/menu/</c>, such as <c>seasonals/summer.jpg</c>.</param>
	public Task<Reply<long>> GetMenuImageAsync(string path, CancellationToken ct)
	{
		return DrainAsync("assets", $"/menu/{path}", ct);
	}

	private async Task<Reply<long>> DrainAsync(string subdomain, string path, CancellationToken ct)
	{
		using var request = endpoint.Request(HttpMethod.Get, subdomain, path);
		return await Exchange.DrainAsync(http, request, ct);
	}
}
