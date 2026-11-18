using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Scores.Persistence;
using Basil.Application.Scores.Storage;
using Basil.Application.Shared.Exceptions;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Persistence;
using Basil.Domain.Authentication;
using Basil.Domain.Game.Client;
using Basil.Domain.Game.Mechanics;
using Basil.Domain.Game.Scores;
using Basil.Domain.Game.Users;
using Basil.Domain.Multiplayer;
using Basil.Shared.Structures.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Basil.Testing;

public sealed class ScoreSubmissionFixture : IDisposable
{
	public const string UninstallId = "uninstall-id";

	public const string DiskSignature = "disk-signature";

	public const string ClientHash = "client-hash";

	public const string VersionDate = "20260101";

	public const string Password = "password";

	public static readonly Md5Hash Beatmap = new("0123456789abcdef0123456789abcdef");

	public static readonly DateTimeOffset PlayedAt = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

	private readonly List<(ScoreRecord Score, byte[] Content)> _replays = [];
	private readonly List<ScoreRecord> _scores = [];

	private readonly Dictionary<(int UserId, GameMode Mode), UserStats> _stats = [];

	private ScoreSubmissionFixture(TestApplication app, StableConnection client, UserRecord user)
	{
		App = app;
		Client = client;
		User = user;
		var provider = app.Provider;

		provider.GetRequiredService<IScoreRepository>()
			.CreateWithPlayAsync(Arg.Any<ScoreData>(), Arg.Any<long>())
			.Returns(call =>
			{
				var data = call.Arg<ScoreData>();
				if (data.Checksum is { } checksum && _scores.Any(score => score.Value.Checksum == checksum))
					throw new AlreadyExistsException("A score with this checksum is already stored.");
				var record = new ScoreRecord { Id = _scores.Count + 1, Value = data };
				_scores.Add(record);

				var key = (UserId: data.UserId!.Value, data.Mode);
				if (!_stats.TryGetValue(key, out var known))
					_stats[key] = known = new UserStats { UserId = key.UserId, Mode = key.Mode };
				known.PlayCount++;
				known.TotalScore += data.LastState.TotalScore;
				known.RankedScore += call.ArgAt<long>(1);
				return Task.FromResult((record, known));
			});

		provider.GetRequiredService<IScoreRepository>()
			.ListAsync(Arg.Any<ScoreQuery>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				var query = call.Arg<ScoreQuery>();
				var best = _scores.Where(score => score.Value.UserId == query.Player?.Id &&
				                                  score.Value.BeatmapHash == query.BeatmapHash)
					.OrderByDescending(score => score.Value.LastState.TotalScore).Take(1).ToList();
				return Task.FromResult(new Page<ScoreRecord>(best, false));
			});

		provider.GetRequiredService<IRoundScoreRepository>()
			.RecordAsync(Arg.Any<RoundScore>())
			.Returns(call => Task.FromResult(call.Arg<RoundScore>()));

		provider.GetRequiredService<IReplayStorage>()
			.SaveAsync(Arg.Any<ScoreRecord>(), Arg.Any<OsuVersion>(), Arg.Any<Stream>())
			.Returns(call =>
			{
				using var copy = new MemoryStream();
				call.Arg<Stream>().CopyTo(copy);
				_replays.Add((call.Arg<ScoreRecord>(), copy.ToArray()));
				return Task.CompletedTask;
			});
	}

	public TestApplication App { get; }

	public StableConnection Client { get; }

	public UserRecord User { get; }

	public IReadOnlyList<ScoreRecord> Scores => _scores;

	public IReadOnlyList<(ScoreRecord Score, byte[] Content)> Replays => _replays;

	public void Dispose()
	{
		App.Dispose();
	}

	public UserStats? StatsOf(GameMode mode)
	{
		return _stats.GetValueOrDefault((User.Id, mode));
	}

	public static async Task<ScoreSubmissionFixture> CreateAsync(string name = "Submitter",
		UserPrivileges? privileges = null)
	{
		var app = new TestApplication();
		var user = await app.CreateUserAsync(name, Password, privileges);
		var result = await app.Users.LoginAsync(
			new LoginAttempt(name, new Md5Hash(Encoding.UTF8.GetBytes(Password))), SessionType.Stable,
			IPAddress.Loopback, LoginClient(), 0);
		var client = Assert.IsType<StableConnection>(result.Connection);
		return new ScoreSubmissionFixture(app, client, user);
	}

	public static OsuClient LoginClient()
	{
		var hash = new Md5Hash("0123456789abcdef0123456789abcdef");
		var fingerprint = new OsuFingerprint(hash,
			Md5Of(UninstallId), Md5Of(DiskSignature),
			new NetworkAdapters { Adapters = "runningunderwine", Hash = hash });
		return new OsuClient(new OsuVersion(new DateOnly(2026, 1, 1), null, OsuVersionStream.Stable),
			fingerprint);
	}

	public static SubmittedClient GenuineClient(Md5Hash? beatmap = null)
	{
		return new SubmittedClient(ClientHash, LoginClient().Fingerprint, UninstallId, DiskSignature, VersionDate,
			beatmap ?? Beatmap);
	}

	public static ScoreData PassedScore()
	{
		return new ScoreData(null, Beatmap, GameMode.Standard, GameMods.NoMod,
			new ScoreState(new HitCounts(100, 0, 0, 0, 0, 0), 1000, 100, 100, true), Grade.S, true, PlayedAt);
	}

	public Submission Genuine(ScoreData score, Md5Hash? storyboard = null)
	{
		return new Submission
		{
			Score = score, Flags = OsuFlags.Clean, HashByClient = SubmissionChecksum(score, User.Value.Name, storyboard)
		};
	}

	public static Md5Hash SubmissionChecksum(ScoreData score, string playerName, Md5Hash? storyboard = null,
		Md5Hash? beatmap = null)
	{
		var hits = score.LastState.HitCounts;
		var timestamp = score.Timestamp.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture);
		var raw = string.Create(CultureInfo.InvariantCulture,
			$"chickenmcnuggets{hits.Num100 + hits.Num300}o15{hits.Num50}{hits.NumGeki}smustard{hits.NumKatu}" +
			$"{hits.NumMiss}uu{beatmap ?? Beatmap}{score.LastState.MaxCombo}{(score.LastState.IsFullCombo ? "True" : "False")}{playerName}" +
			$"{score.LastState.TotalScore}{score.Grade.ToString().ToUpperInvariant()}{(int)score.Mods}Q" +
			$"{(score.IsPassed ? "True" : "False")}{(int)score.Mode}{VersionDate}{timestamp}{ClientHash}" +
			$"{storyboard?.ToString() ?? string.Empty}");
		return new Md5Hash(Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(raw))));
	}

	private static Md5Hash Md5Of(string text)
	{
		return new Md5Hash(Encoding.UTF8.GetBytes(text));
	}
}