using System.Globalization;
using System.Net.Http.Json;
using Basil.Protocol.Game.Stable.Models.Web;
using Basil.Tests.Shared.Clients.Stable;

namespace Basil.Tests.Shared.Clients.OsuWeb;

/// <summary>The web endpoints an osu! client calls on the <c>osu.</c> host, signed with the identity of its account.</summary>
/// <remarks>
///     Most calls only work while the account has an open <see cref="StableClient" /> session from the same address.
///     A refused call is an answer; an upload the server refuses by cutting the connection throws a transport exception.
/// </remarks>
public sealed class OsuWebClient(Endpoint endpoint, HttpClient http, StableIdentity identity)
{
	private const string Host = "osu";

	/// <summary>Asks for the leaderboard of a beatmap.</summary>
	/// <param name="beatmapMd5">The md5 of the beatmap.</param>
	/// <param name="mode">The game mode number.</param>
	/// <param name="mods">The mods, used when <paramref name="rankedType" /> is 3.</param>
	/// <param name="rankedType">The kind of board: 1 for the top scores, 3 for those with the mods.</param>
	/// <returns>
	///     The answer with the board, or <see langword="null" /> for a beatmap the server does not know;
	///     <see cref="Reply{T}.Status" /> tells a refusal.
	/// </returns>
	public async Task<Reply<LeaderboardReply?>> GetScoresAsync(string beatmapMd5, int mode, int mods,
		int rankedType, CancellationToken ct)
	{
		using var request = endpoint.Request(HttpMethod.Get, Host,
			$"/web/osu-osz2-getscores.php?us={Escape(identity.Name)}&ha={identity.PasswordMd5}&c={beatmapMd5}&m={mode}&mods={mods}&v={rankedType}");
		var text = await Exchange.TextAsync(http, request, ct);
		return new Reply<LeaderboardReply?>(text.Status, text.IsSuccess ? LeaderboardText.Parse(text.Body) : null);
	}

	/// <summary>Submits a finished play with its replay.</summary>
	/// <returns>
	///     The answer text: the charts of an accepted play, or <c>error: no</c> for a refusal, which is an answer with
	///     status 200.
	/// </returns>
	public async Task<Reply<string>> SubmitScoreAsync(PlayedScore score, byte[] replay, CancellationToken ct)
	{
		using var request = endpoint.Request(HttpMethod.Post, Host, "/web/osu-submit-modular-selector.php");
		using var form = ScoreSubmissionForm.Build(score, identity, replay);
		request.Content = form;
		return await Exchange.TextAsync(http, request, ct);
	}

	/// <summary>Downloads the replay of a score and counts its bytes.</summary>
	public async Task<Reply<long>> GetReplayAsync(long scoreId, CancellationToken ct)
	{
		using var request =
			Get($"/web/osu-getreplay.php?u={Escape(identity.Name)}&h={identity.PasswordMd5}&c={scoreId}");
		return await Exchange.DrainAsync(http, request, ct);
	}

	/// <summary>Searches beatmapsets as osu!direct does.</summary>
	/// <param name="query">The search text.</param>
	/// <param name="status">The osu!direct status filter number; 4 means all.</param>
	/// <param name="mode">The game mode number, or -1 for all.</param>
	/// <param name="page">The page, from 0.</param>
	public async Task<Reply<string>> SearchAsync(string query, int status, int mode, int page, CancellationToken ct)
	{
		using var request = Get(
			$"/web/osu-search.php?u={Escape(identity.Name)}&h={identity.PasswordMd5}&r={status}&q={Escape(query)}&m={mode}&p={page}");
		return await Exchange.TextAsync(http, request, ct);
	}

	/// <summary>Reads the osu!direct line of one beatmapset.</summary>
	public async Task<Reply<string>> SearchSetAsync(int setId, CancellationToken ct)
	{
		using var request =
			Get($"/web/osu-search-set.php?u={Escape(identity.Name)}&h={identity.PasswordMd5}&s={setId}");
		return await Exchange.TextAsync(http, request, ct);
	}

	/// <summary>Downloads a beatmapset archive and counts its bytes.</summary>
	/// <param name="setId">The beatmapset id.</param>
	/// <param name="withVideo">Whether the archive keeps the video.</param>
	public async Task<Reply<long>> DownloadSetAsync(int setId, bool withVideo, CancellationToken ct)
	{
		using var request =
			Get($"/d/{setId}{(withVideo ? string.Empty : "n")}?u={Escape(identity.Name)}&h={identity.PasswordMd5}");
		return await Exchange.DrainAsync(http, request, ct);
	}

	/// <summary>Uploads a screenshot.</summary>
	/// <param name="picture">A PNG or JPEG file.</param>
	/// <returns>The answer text, which names the stored picture.</returns>
	public async Task<Reply<string>> UploadScreenshotAsync(byte[] picture, CancellationToken ct)
	{
		using var request = endpoint.Request(HttpMethod.Post, Host, "/web/osu-screenshot.php");
		using var form = new MultipartFormDataContent
		{
			{ new StringContent(identity.Name), "u" },
			{ new StringContent(identity.PasswordMd5), "p" },
			{ new ByteArrayContent(picture), "ss", "screenshot.png" }
		};
		request.Content = form;
		return await Exchange.TextAsync(http, request, ct);
	}

	/// <summary>Reads the ids of the friends of the account.</summary>
	public async Task<Reply<IReadOnlyList<int>>> GetFriendsAsync(CancellationToken ct)
	{
		using var request = Get($"/web/osu-getfriends.php?u={Escape(identity.Name)}&h={identity.PasswordMd5}");
		var text = await Exchange.TextAsync(http, request, ct);
		IReadOnlyList<int> ids = text.IsSuccess
			?
			[
				.. text.Body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
					.Select(line => int.Parse(line, CultureInfo.InvariantCulture))
			]
			: [];
		return new Reply<IReadOnlyList<int>>(text.Status, ids);
	}

	/// <summary>Reads the addresses of the seasonal background pictures.</summary>
	public async Task<Reply<IReadOnlyList<string>>> GetSeasonalsAsync(CancellationToken ct)
	{
		using var request = Get("/web/osu-getseasonal.php");
		using var response = await http.SendAsync(request, ct);
		IReadOnlyList<string> urls = response.IsSuccessStatusCode
			? await response.Content.ReadFromJsonAsync<string[]>(ct) ?? []
			: [];
		return new Reply<IReadOnlyList<string>>(response.StatusCode, urls);
	}

	/// <summary>Asks for the star rating of a beatmap with mods.</summary>
	/// <returns>The answer text, a JSON object with the rating.</returns>
	public async Task<Reply<string>> GetDifficultyRatingAsync(int beatmapId, int mods, CancellationToken ct)
	{
		using var request = endpoint.Request(HttpMethod.Post, Host, $"/difficulty-rating?b={beatmapId}&mods={mods}");
		return await Exchange.TextAsync(http, request, ct);
	}

	/// <summary>Asks which beatmaps the server knows by file name.</summary>
	/// <returns>The answer text, one line per known beatmap.</returns>
	public async Task<Reply<string>> GetBeatmapInfoAsync(IReadOnlyList<string> fileNames, CancellationToken ct)
	{
		using var request = endpoint.Request(HttpMethod.Post, Host,
			$"/web/osu-getbeatmapinfo.php?u={Escape(identity.Name)}&h={identity.PasswordMd5}");
		request.Content = JsonContent.Create(new { Filenames = fileNames });
		return await Exchange.TextAsync(http, request, ct);
	}

	private HttpRequestMessage Get(string pathAndQuery)
	{
		return endpoint.Request(HttpMethod.Get, Host, pathAndQuery);
	}

	private static string Escape(string value)
	{
		return Uri.EscapeDataString(value);
	}
}