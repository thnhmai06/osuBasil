using Basil.Application.Scores.Objects;
using Basil.Application.Scores.Persistence;
using Basil.Application.Shared.Privileges;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Game.Scores;
using Basil.Domain.Users;
using Basil.Domain.Game.Users;

namespace Basil.Application.Scores.Capabilities;

public readonly struct ScopedScoreSubmitter
{
	public StableSession Actor { get; }

	public UserPrivileges Privileges { get; }

	internal bool Lacks(UserPrivileges required)
	{
		return ScopedPrivileges.Lacks(Privileges, required);
	}

	private readonly ScoreSubmitter _submitter;

	internal ScopedScoreSubmitter(ScoreSubmitter submitter, StableSession actor, UserPrivileges privileges)
	{
		Actor = actor;
		Privileges = privileges;
		_submitter = submitter;
	}

	public async Task<(ScoreSubmissionOutcome Result, SubmissionRejectionReason? Rejection, ScoreReceipt? Receipt)> SubmitAsync(
		Submission submission,
		BeatmapChecksums? beatmap,
		SubmittedClient client,
		Stream? replay,
		CancellationToken ct = default)
	{
		if (Lacks(UserPrivileges.Player))
			return (ScoreSubmissionOutcome.Denied, null, null);

		return await _submitter.SubmitAsync(Actor, submission, beatmap, client, replay, ct);
	}
}