using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Paddings;
using Org.BouncyCastle.Crypto.Parameters;

namespace Basil.Testing.Clients;

/// <summary>
///     Behaves like the osu! stable client over HTTP: logs in, keeps the <c>osu-token</c> header, polls, sends
///     packets and calls the web endpoints. It writes the client's packets by hand from the osu! protocol so the
///     server is tested against the wire, not against its own code.
/// </summary>
public sealed class OsuClient : IDisposable
{
	public const string UninstallId = "uninstall-id-1";
	public const string DiskSignature = "disk-signature-1";
	private const string BuildDate = "20260101";
	private readonly string _banchoHost;

	private readonly HttpClient _http;
	private readonly string _osuVersion;
	private readonly List<ServerPacket> _received = [];
	private readonly string _webHost;

	private OsuClient(Uri address, string password, string name, bool tourney, string domain)
	{
		_http = new HttpClient { BaseAddress = address };
		_banchoHost = $"c.{domain}";
		_webHost = $"osu.{domain}";
		Name = name;
		Password = password;
		_osuVersion = $"b{BuildDate}.1{(tourney ? "tourney" : string.Empty)}";
	}

	public string Name { get; }

	public string Password { get; }

	public string? Token { get; private set; }

	/// <summary>Gets the user id from the login reply: positive on success, negative failure reason otherwise.</summary>
	public int LoginCode { get; private set; }

	/// <summary>Gets every packet received so far, in order.</summary>
	public IReadOnlyList<ServerPacket> Received => _received;

	public void Dispose()
	{
		_http.Dispose();
	}

	public static string PasswordMd5(string password)
	{
		return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(password)));
	}

	/// <summary>Creates a client that has not logged in.</summary>
	public static OsuClient Create(BasilHost host, string name, string password, bool tourney = false)
	{
		return new OsuClient(host.Url, password, name, tourney, host.Domain);
	}

	/// <summary>Logs in the way the stable client does.</summary>
	public async Task<IReadOnlyList<ServerPacket>> LoginAsync(string? passwordOverride = null,
		string? forwardedFor = null)
	{
		var clientHash =
			$"{Md5Of("osu-path")}:runningunderwine:{Md5Of("adapters")}:{Md5Of(UninstallId)}:{Md5Of(DiskSignature)}:";
		var body = $"{Name}\n{PasswordMd5(passwordOverride ?? Password)}\n{_osuVersion}|0|1|{clientHash}|0\n";
		using var request = Bancho(HttpMethod.Post, body);
		if (forwardedFor is not null) request.Headers.Add("X-Forwarded-For", forwardedFor);
		using var response = await _http.SendAsync(request);
		var packets = ServerPacket.Split(await response.Content.ReadAsByteArrayAsync());
		_received.AddRange(packets);
		Token = response.Headers.TryGetValues("cho-token", out var values) ? values.First() : null;
		LoginCode = packets.FirstOrDefault(packet => packet.Id == In.UserId)?.AsInt() ?? 0;
		return packets;
	}

	/// <summary>Posts packets (or none, to just poll) and returns what the server answered.</summary>
	public async Task<IReadOnlyList<ServerPacket>> SendAsync(Action<ClientPacketWriter>? write = null)
	{
		var writer = new ClientPacketWriter();
		write?.Invoke(writer);
		using var request = Bancho(HttpMethod.Post, writer.ToArray());
		request.Headers.Add("osu-token", Token);
		using var response = await _http.SendAsync(request);
		var packets = ServerPacket.Split(await response.Content.ReadAsByteArrayAsync());
		_received.AddRange(packets);
		return packets;
	}

	public Task<IReadOnlyList<ServerPacket>> PollAsync()
	{
		return SendAsync();
	}

	/// <summary>Sends a request with an arbitrary token (for the unknown-token case).</summary>
	public async Task<IReadOnlyList<ServerPacket>> SendWithTokenAsync(string token)
	{
		using var request = Bancho(HttpMethod.Post, []);
		request.Headers.Add("osu-token", token);
		using var response = await _http.SendAsync(request);
		return ServerPacket.Split(await response.Content.ReadAsByteArrayAsync());
	}

	/// <summary>Removes the packets received so far from the record.</summary>
	public void ClearReceived()
	{
		_received.Clear();
	}

	/// <summary>Polls until a packet with the id (and matching the filter) has been received, or the polls run out.</summary>
	public async Task<ServerPacket?> WaitForAsync(int id, Func<ServerPacket, bool>? where = null, int polls = 40)
	{
		bool Matches(ServerPacket packet)
		{
			return packet.Id == id && (where?.Invoke(packet) ?? true);
		}

		for (var index = 0; index < polls; index++)
		{
			if (_received.FirstOrDefault(Matches) is { } found) return found;
			await PollAsync();
			if (_received.FirstOrDefault(Matches) is { } arrived) return arrived;
			await Task.Delay(25);
		}

		return null;
	}

	/// <summary>Polls until a condition on what has been received holds, or the polls run out.</summary>
	/// <param name="condition">What the received packets must satisfy.</param>
	/// <param name="polls">How many times to poll before giving up.</param>
	/// <returns>Whether the condition held.</returns>
	public async Task<bool> WaitUntilAsync(Func<bool> condition, int polls = 40)
	{
		for (var index = 0; index < polls; index++)
		{
			if (condition()) return true;
			await PollAsync();
			if (condition()) return true;
			await Task.Delay(25);
		}

		return false;
	}

	public Task<IReadOnlyList<ServerPacket>> LogoutAsync()
	{
		return SendAsync(writer => writer.Packet(Out.Logout));
	}

	// --- web endpoints (osu.) ---

	/// <summary>Calls GET on the web host with the client's credentials added.</summary>
	public async Task<(HttpStatusCode Status, string Text)> WebGetAsync(string path, string userKey, string hashKey,
		params (string Key, string Value)[] query)
	{
		return await WebGetAsync(path, userKey, hashKey, null, query);
	}

	/// <summary>Calls GET on the web host with the client's credentials added, from the given forwarded address.</summary>
	public async Task<(HttpStatusCode Status, string Text)> WebGetAsync(string path, string userKey, string hashKey,
		string? forwardedFor, params (string Key, string Value)[] query)
	{
		var parts = new List<(string, string)>(query) { (userKey, Name), (hashKey, PasswordMd5(Password)) };
		var url = path + "?" +
		          string.Join('&', parts.Select(part => $"{part.Item1}={Uri.EscapeDataString(part.Item2)}"));
		using var request = new HttpRequestMessage(HttpMethod.Get, url);
		request.Headers.Host = _webHost;
		if (forwardedFor is not null) request.Headers.Add("X-Forwarded-For", forwardedFor);
		using var response = await _http.SendAsync(request);
		return (response.StatusCode, await response.Content.ReadAsStringAsync());
	}

	public Task<(HttpStatusCode Status, string Text)> GetScoresAsync(string beatmapMd5, int mode = 0, int mods = 0)
	{
		return WebGetAsync("/web/osu-osz2-getscores.php", "us", "ha", ("c", beatmapMd5), ("m", mode.ToString()),
			("mods", mods.ToString()), ("v", "1"));
	}

	public Task<(HttpStatusCode Status, string Text)> GetFriendsAsync(string? forwardedFor = null)
	{
		return WebGetAsync("/web/osu-getfriends.php", "u", "h", forwardedFor);
	}

	public Task<(HttpStatusCode Status, string Text)> LastFmAsync(string b)
	{
		return WebGetAsync("/web/lastfm.php", "us", "ha", ("b", b));
	}

	public Task<(HttpStatusCode Status, string Text)> SearchSetAsync(int setId)
	{
		return WebGetAsync("/web/osu-search-set.php", "u", "h", ("s", setId.ToString()));
	}

	public async Task<(HttpStatusCode Status, string Text)> GetBeatmapInfoAsync(params string[] filenames)
	{
		var url = $"/web/osu-getbeatmapinfo.php?u={Uri.EscapeDataString(Name)}&h={PasswordMd5(Password)}";
		using var request = new HttpRequestMessage(HttpMethod.Post, url)
		{
			Content = new StringContent("{\"Filenames\":[" + string.Join(',', filenames.Select(f => $"\"{f}\"")) + "]}")
		};
		request.Headers.Host = _webHost;
		using var response = await _http.SendAsync(request);
		return (response.StatusCode, await response.Content.ReadAsStringAsync());
	}

	public async Task<(HttpStatusCode Status, string Text)> UploadScreenshotAsync(byte[] image)
	{
		using var form = new MultipartFormDataContent
		{
			{ new StringContent(Name), "u" },
			{ new StringContent(PasswordMd5(Password)), "p" },
			{ new ByteArrayContent(image), "ss", "screenshot.png" }
		};
		using var request = new HttpRequestMessage(HttpMethod.Post, "/web/osu-screenshot.php") { Content = form };
		request.Headers.Host = _webHost;
		using var response = await _http.SendAsync(request);
		return (response.StatusCode, await response.Content.ReadAsStringAsync());
	}

	/// <summary>Registers an account the way the client's registration form does.</summary>
	public static async Task<(HttpStatusCode Status, string Text)> RegisterAsync(BasilHost host, string name,
		string password, bool checkOnly = false, string? forwardedFor = null)
	{
		using var http = new HttpClient { BaseAddress = host.Url };
		using var request = new HttpRequestMessage(HttpMethod.Post, "/users")
		{
			Content = new FormUrlEncodedContent(new Dictionary<string, string>
			{
				["user[username]"] = name, ["user[user_email]"] = $"{name}@example.invalid",
				["user[password]"] = password, ["check"] = checkOnly ? "1" : "0"
			})
		};
		request.Headers.Host = $"osu.{host.Domain}";
		if (forwardedFor is not null) request.Headers.Add("X-Forwarded-For", forwardedFor);
		using var response = await http.SendAsync(request);
		return (response.StatusCode, await response.Content.ReadAsStringAsync());
	}

	/// <summary>Submits a score as the client does: encrypted score data, client hash and replay.</summary>
	public async Task<(HttpStatusCode Status, string Text)> SubmitScoreAsync(string beatmapMd5, int score,
		bool passed = true, int mods = 0, int n300 = 100, string grade = "S", string? checksumOverride = null,
		byte[]? replay = null)
	{
		var timestamp = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
		var clientHash =
			$"{Md5Of("osu-path")}:runningunderwine:{Md5Of("adapters")}:{Md5Of(UninstallId)}:{Md5Of(DiskSignature)}:";
		var ts = timestamp.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture);
		var raw = string.Create(CultureInfo.InvariantCulture,
			$"chickenmcnuggets{n300}o150{0}smustard00uu{beatmapMd5}{n300}True{Name}{score}{grade.ToUpperInvariant()}{mods}Q{(passed ? "True" : "False")}0{BuildDate}{ts}{clientHash}");
		var checksum = checksumOverride ?? Md5Of(raw);
		var data = string.Create(CultureInfo.InvariantCulture,
			$"{beatmapMd5}:{Name} :{checksum}:{n300}:0:0:0:0:0:{score}:{n300}:True:{grade}:{mods}:{(passed ? "True" : "False")}:0:{ts}:");
		var iv = RandomNumberGenerator.GetBytes(32);
		using var form = new MultipartFormDataContent
		{
			{ new StringContent(Encrypt(data, iv)), "score" },
			{ new StringContent(Encrypt(clientHash, iv)), "s" },
			{ new StringContent(Convert.ToBase64String(iv)), "iv" },
			{ new StringContent(BuildDate), "osuver" },
			{ new StringContent(PasswordMd5(Password)), "pass" },
			{ new StringContent($"{UninstallId}|{DiskSignature}"), "c1" },
			{ new StringContent("0"), "st" },
			{ new StringContent("0"), "ft" }
		};
		if (replay is not null)
			form.Add(
				new ByteArrayContent(replay)
					{ Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") } },
				"score", "replay");
		using var request = new HttpRequestMessage(HttpMethod.Post, "/web/osu-submit-modular-selector.php")
			{ Content = form };
		request.Headers.Host = _webHost;
		using var response = await _http.SendAsync(request);
		return (response.StatusCode, await response.Content.ReadAsStringAsync());
	}

	private string Encrypt(string text, byte[] iv)
	{
		var key = Encoding.UTF8.GetBytes($"osu!-scoreburgr---------{BuildDate}");
		var cipher = new PaddedBufferedBlockCipher(new CbcBlockCipher(new RijndaelEngine(256)), new Pkcs7Padding());
		cipher.Init(true, new ParametersWithIV(new KeyParameter(key), iv));
		var input = Encoding.UTF8.GetBytes(text);
		var output = new byte[cipher.GetOutputSize(input.Length)];
		var length = cipher.ProcessBytes(input, 0, input.Length, output, 0);
		length += cipher.DoFinal(output, length);
		return Convert.ToBase64String(output, 0, length);
	}

	public static string Md5Of(string text)
	{
		return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(text)));
	}

	private HttpRequestMessage Bancho(HttpMethod method, string body)
	{
		return Bancho(method, Encoding.UTF8.GetBytes(body));
	}

	private HttpRequestMessage Bancho(HttpMethod method, byte[] body)
	{
		var request = new HttpRequestMessage(method, "/") { Content = new ByteArrayContent(body) };
		request.Headers.Host = _banchoHost;
		return request;
	}

	/// <summary>The client packet ids the tests send.</summary>
	public static class Out
	{
		public const int ChangeAction = 0,
			SendPublicMessage = 1,
			Logout = 2,
			RequestStatusUpdate = 3,
			Ping = 4,
			StartSpectating = 16,
			StopSpectating = 17,
			SpectateFrames = 18,
			CantSpectate = 21,
			SendPrivateMessage = 25,
			PartLobby = 29,
			JoinLobby = 30,
			CreateMatch = 31,
			JoinMatch = 32,
			PartMatch = 33,
			MatchChangeSlot = 38,
			MatchReady = 39,
			MatchLock = 40,
			MatchChangeSettings = 41,
			MatchStart = 44,
			MatchScoreUpdate = 47,
			MatchComplete = 49,
			MatchChangeMods = 51,
			MatchLoadComplete = 52,
			MatchNoBeatmap = 54,
			MatchNotReady = 55,
			MatchFailed = 56,
			MatchHasBeatmap = 59,
			MatchSkipRequest = 60,
			ChannelJoin = 63,
			MatchTransferHost = 70,
			FriendAdd = 73,
			FriendRemove = 74,
			MatchChangeTeam = 77,
			ChannelPart = 78,
			SetAwayMessage = 82,
			UserStatsRequest = 85,
			MatchInvite = 87,
			MatchChangePassword = 90,
			TournamentMatchInfoRequest = 93,
			UserPresenceRequest = 97,
			UserPresenceRequestAll = 98,
			ToggleBlockNonFriendDms = 99,
			TournamentJoinMatchChannel = 108,
			TournamentLeaveMatchChannel = 109;
	}

	/// <summary>The server packet ids the tests look for.</summary>
	public static class In
	{
		public const int UserId = 5,
			SendMessage = 7,
			UserStats = 11,
			UserLogout = 12,
			SpectatorJoined = 13,
			SpectatorLeft = 14,
			SpectateFrames = 15,
			SpectatorCantSpectate = 22,
			Notification = 24,
			UpdateMatch = 26,
			NewMatch = 27,
			DisposeMatch = 28,
			MatchJoinSuccess = 36,
			MatchJoinFail = 37,
			FellowSpectatorJoined = 42,
			FellowSpectatorLeft = 43,
			MatchStart = 46,
			MatchScoreUpdate = 48,
			MatchTransferHost = 50,
			MatchAllPlayersLoaded = 53,
			MatchPlayerFailed = 57,
			MatchComplete = 58,
			MatchSkip = 61,
			ChannelJoin = 64,
			ChannelInfo = 65,
			ChannelKick = 66,
			Privileges = 71,
			FriendsList = 72,
			ProtocolVersion = 75,
			MatchPlayerSkipped = 81,
			UserPresence = 83,
			Restart = 86,
			MatchInvite = 88,
			ChannelInfoEnd = 89,
			UserDmBlocked = 100,
			TargetIsSilenced = 101,
			AccountRestricted = 104,
			MatchAbort = 106;
	}
}