using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Basil.Application.Common;
using Basil.Application.Common.Common;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Scores.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Connections;
using Basil.Domain.Auth;
using Basil.Domain.Client;
using Basil.Domain.Common;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Basil.Testing;

/// <summary>
///     A game client that has logged in and can submit scores, with in-memory score, replay and statistics storage
///     that behaves as the persistence ports document.
/// </summary>
/// <remarks>
///     The submission checksum is written here from the legacy osu! formula, independently of the code under test,
///     so a valid submission is accepted and a changed field is refused.
/// </remarks>
public sealed class ScoreSubmissionFixture : IDisposable
{
	/// <summary>The uninstall id the client reported at login.</summary>
	public const string UninstallId = "uninstall-id";

	/// <summary>The disk signature the client reported at login.</summary>
	public const string DiskSignature = "disk-signature";

	/// <summary>The client hash the client sends with its submissions.</summary>
	public const string ClientHash = "client-hash";

	/// <summary>The version date of the client, as a submission writes it.</summary>
	public const string VersionDate = "20260101";

	/// <summary>The password of the account.</summary>
	public const string Password = "password";

	/// <summary>The beatmap the submissions are checked against.</summary>
	public static readonly Md5 Beatmap = new("0123456789abcdef0123456789abcdef");

	/// <summary>A timestamp for the scores.</summary>
	public static readonly DateTimeOffset PlayedAt = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

	private readonly List<(ScoreRecord Score, byte[] Content)> _replays = [];
	private readonly List<ScoreRecord> _scores = [];

	private readonly Dictionary<(int UserId, GameMode Mode), UserStats> _stats = [];

	private ScoreSubmissionFixture(TestApplication app, BanchoConnection client, UserRecord user)
	{
		App = app;
		Client = client;
		User = user;
		var provider = app.Provider;

		provider.GetRequiredService<IScoreRepository>()
			.CreateAsync(Arg.Any<ScoreData>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				var data = call.Arg<ScoreData>();
				if (data.Checksum is { } checksum && _scores.Any(score => score.Value.Checksum == checksum))
					throw new AlreadyExistsException("A score with this checksum is already stored.");
				var record = new ScoreRecord { Id = _scores.Count + 1, Value = data };
				_scores.Add(record);
				return Task.FromResult(record);
			});

		provider.GetRequiredService<IRoundScoreRepository>()
			.RecordAsync(Arg.Any<RoundScore>(), Arg.Any<CancellationToken>())
			.Returns(call => Task.FromResult(call.Arg<RoundScore>()));

		provider.GetRequiredService<IReplayStorage>()
			.SaveAsync(Arg.Any<ScoreRecord>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				using var copy = new MemoryStream();
				call.Arg<Stream>().CopyTo(copy);
				_replays.Add((call.Arg<ScoreRecord>(), copy.ToArray()));
				return Task.CompletedTask;
			});

		var stats = provider.GetRequiredService<IUserStatsRepository>();
		stats.AddPlayAsync(Arg.Any<UserRecord>(), Arg.Any<GameMode>(), Arg.Any<long>(), Arg.Any<long>(),
				Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				var key = (UserId: call.Arg<UserRecord>().Id, Mode: call.ArgAt<GameMode>(1));
				if (!_stats.TryGetValue(key, out var known))
					_stats[key] = known = new UserStats { UserId = key.UserId, Mode = key.Mode };
				known.PlayCount++;
				known.TotalScore += call.ArgAt<long>(2);
				known.RankedScore += call.ArgAt<long>(3);
				return ValueTask.FromResult(known);
			});
	}

	/// <summary>Gets the application the client is logged in to.</summary>
	public TestApplication App { get; }

	/// <summary>Gets the game client connection.</summary>
	public BanchoConnection Client { get; }

	/// <summary>Gets the account of the client.</summary>
	public UserRecord User { get; }

	/// <summary>Gets the scores stored so far, in storage order.</summary>
	public IReadOnlyList<ScoreRecord> Scores => _scores;

	/// <summary>Gets the replays stored so far with the bytes they were stored with.</summary>
	public IReadOnlyList<(ScoreRecord Score, byte[] Content)> Replays => _replays;

	/// <inheritdoc />
	public void Dispose()
	{
		App.Dispose();
	}

	/// <summary>Gets the statistics of the account in a mode.</summary>
	/// <param name="mode">The game mode.</param>
	/// <returns>The stored statistics, or <see langword="null" /> when none were stored.</returns>
	public UserStats? StatsOf(GameMode mode)
	{
		return _stats.GetValueOrDefault((User.Id, mode));
	}

	/// <summary>Creates an account, logs it in with a game client and prepares storage.</summary>
	/// <param name="name">The account name.</param>
	/// <param name="permissions">The permissions to grant, or the normal new-account permissions.</param>
	/// <returns>The fixture.</returns>
	public static async Task<ScoreSubmissionFixture> CreateAsync(string name = "Submitter",
		UserPermissions? permissions = null)
	{
		var app = new TestApplication();
		var user = await app.CreateUserAsync(name, Password, permissions);
		var result = await app.Users.LoginAsync(
			new LoginAttempt(name, new Md5(Encoding.UTF8.GetBytes(Password))), SessionType.Osu,
			IPAddress.Loopback, LoginClient(), 0);
		var client = Assert.IsType<BanchoConnection>(result.Connection);
		return new ScoreSubmissionFixture(app, client, user);
	}

	/// <summary>Gets the client descriptor the account logged in with.</summary>
	/// <returns>The client descriptor.</returns>
	public static OsuClient LoginClient()
	{
		var hash = new Md5("0123456789abcdef0123456789abcdef");
		var fingerprint = new OsuFingerprint(hash,
			Md5Of(UninstallId), Md5Of(DiskSignature), new NetworkAdapters { Adapters = "runningunderwine", Hash = hash });
		return new OsuClient(new OsuVersion(new DateOnly(2026, 1, 1), null, OsuVersionStream.Stable),
			fingerprint);
	}

	/// <summary>Creates the client information a genuine client sends with a score.</summary>
	/// <param name="beatmap">The beatmap the client played.</param>
	/// <returns>The client information.</returns>
	public static SubmittedClient GenuineClient(Md5? beatmap = null)
	{
		return new SubmittedClient(ClientHash, LoginClient().Fingerprint, UninstallId, DiskSignature, VersionDate,
			beatmap ?? Beatmap);
	}

	/// <summary>Creates a passed standard-mode score of 100 great hits.</summary>
	/// <returns>The score.</returns>
	public static ScoreData PassedScore()
	{
		return new ScoreData(null, Beatmap, GameMode.Standard, GameMods.NoMod, new HitCounts(100, 0, 0, 0, 0, 0),
			1000, 100, 100, Grade.S, true, true, PlayedAt);
	}

	/// <summary>Wraps a score in a submission whose checksum is the one a genuine client computes.</summary>
	/// <param name="score">The score.</param>
	/// <param name="storyboard">The storyboard checksum of the beatmap, if any.</param>
	/// <returns>The submission.</returns>
	public Submission Genuine(ScoreData score, Md5? storyboard = null)
	{
		return new Submission { Score = score, Flags = OsuFlags.Clean, HashByClient = SubmissionChecksum(score, User.Value.Name, storyboard) };
	}

	/// <summary>Computes the checksum the osu! client sends for a score (the legacy formula of the osu! server).</summary>
	/// <param name="score">The score.</param>
	/// <param name="playerName">The player's name.</param>
	/// <param name="storyboard">The storyboard checksum of the beatmap, if any.</param>
	/// <param name="beatmap">The beatmap the score is checked against, or <see cref="Beatmap" />.</param>
	/// <returns>The checksum.</returns>
	public static Md5 SubmissionChecksum(ScoreData score, string playerName, Md5? storyboard = null,
		Md5? beatmap = null)
	{
		var hits = score.HitCounts;
		var timestamp = score.Timestamp.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture);
		var raw = string.Create(CultureInfo.InvariantCulture,
			$"chickenmcnuggets{hits.Num100 + hits.Num300}o15{hits.Num50}{hits.NumGeki}smustard{hits.NumKatu}" +
			$"{hits.NumMiss}uu{beatmap ?? Beatmap}{score.MaxCombo}{(score.IsFullCombo ? "True" : "False")}{playerName}" +
			$"{score.TotalScore}{score.Grade.ToString().ToUpperInvariant()}{(int)score.Mods}Q" +
			$"{(score.IsPassed ? "True" : "False")}{(int)score.Mode}{VersionDate}{timestamp}{ClientHash}" +
			$"{storyboard?.ToString() ?? string.Empty}");
		return new Md5(Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(raw))));
	}

	private static Md5 Md5Of(string text)
	{
		return new Md5(Encoding.UTF8.GetBytes(text));
	}
}