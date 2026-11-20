using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Rulesets.Messages;
using Basil.Application.Scores.Persistence;
using Basil.Application.Shared.Settings;
using Basil.Application.Shared.Waiting;
using Basil.Domain.Scores;
using Basil.Shared.Structures.Cryptography;
using Mediator;

namespace Basil.Application.Scores.Objects;

/// <summary>Asks the rulesets for the pp of plays and writes the answers to the scores they belong to.</summary>
public sealed class ScorePerformanceRequests
{
	private readonly ISender _sender;
	private readonly IBeatmapRepository _beatmaps;
	private readonly IScoreRepository _scores;
	private readonly ISettingsRepository _settings;
	private readonly Pending<Guid, double?> _pending;

	internal ScorePerformanceRequests(ISender sender, IBeatmapRepository beatmaps, IScoreRepository scores,
		ISettingsRepository settings, TimeProvider clock)
	{
		_sender = sender;
		_beatmaps = beatmaps;
		_scores = scores;
		_settings = settings;
		_pending = new Pending<Guid, double?>(clock);
	}

	/// <summary>Asks for the pp of a play that is not stored yet and waits for them, up to the configured limit.</summary>
	/// <returns>The pp when they arrived in time; the key to hand to <see cref="StoredAsync"/> once the score is stored when they did not (null when they never will).</returns>
	internal async Task<(Guid? Key, double? Pp)> RequestAndWaitAsync(ScoreData play, CancellationToken ct)
	{
		if (!play.IsPassed || play.BeatmapHash is not { } hash ||
		    await _beatmaps.GetAsync(hash, ct) is not { } beatmap)
			return (null, null);

		// The waiter exists before the request goes out, so the answer cannot arrive unseen.
		var key = Guid.NewGuid();
		_pending.Register(key);
		double? pp;
		try
		{
			await SendAsync(key, beatmap.Id, hash, play, ct);
			var limit = (await _settings.GetAsync(ct)).Beatmaps.ScoreWait;
			bool arrived;
			(arrived, pp) = await _pending.WaitAsync(key, limit, ct);
			if (!arrived) return (key, null);
		}
		catch
		{
			_pending.Discard(key);
			throw;
		}

		_pending.Discard(key);
		return (null, pp);
	}

	/// <summary>Asks for the pp of a score that is already stored; they are written to it when they arrive.</summary>
	/// <returns>False when the score has no pp to calculate: it was failed, or its beatmap is not held.</returns>
	internal async Task<bool> RequestAsync(ScoreRecord score, CancellationToken ct)
	{
		var play = score.Value;
		if (!play.IsPassed || play.BeatmapHash is not { } hash ||
		    await _beatmaps.GetAsync(hash, ct) is not { } beatmap)
			return false;

		var key = Guid.NewGuid();
		_pending.RegisterStored(key, score.Id);
		try
		{
			await SendAsync(key, beatmap.Id, hash, play, ct);
		}
		catch
		{
			_pending.Discard(key);
			throw;
		}

		return true;
	}

	/// <summary>Says that the score of a request that gave up is stored; pp that arrived meanwhile are written to it now.</summary>
	internal async Task StoredAsync(Guid key, int scoreId)
	{
		var (arrived, pp) = _pending.Store(key, scoreId);
		if (arrived && pp is { } value) await _scores.SetPpAsync(scoreId, value);
	}

	/// <summary>Forgets a request whose score was refused.</summary>
	internal void Forget(Guid key)
	{
		_pending.Discard(key);
	}

	/// <summary>Takes an answer: hands it to the submission still waiting for it, or writes it to the stored score.</summary>
	internal async Task CalculatedAsync(Guid key, double? pp)
	{
		var (outcome, scoreId) = _pending.Complete(key, pp);
		if (outcome == PendingOutcome.Stored && pp is { } value) await _scores.SetPpAsync(scoreId, value);
	}

	private Task SendAsync(Guid key, int beatmapId, Md5Hash hash, ScoreData play, CancellationToken ct)
	{
		return _sender.Send(new CalculateScorePerformance(key, beatmapId, hash, play.Mode, play.Mods,
			play.LastState.HitCounts, play.LastState.MaxCombo), ct).AsTask();
	}
}