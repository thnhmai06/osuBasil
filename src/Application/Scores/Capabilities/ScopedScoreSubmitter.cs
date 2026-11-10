using Basil.Application.Common;
using Basil.Application.Scores.Objects;
using Basil.Application.Scores.Persistence;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Scores.Capabilities;

/// <summary>The score submission as one game client may do it.</summary>
public readonly struct ScopedScoreSubmitter
{
	/// <summary>Gets the actor this scope is for.</summary>
	public StableConnection Actor { get; }

	/// <summary>Gets the privileges the actor held when the scope was created.</summary>
	public UserPrivileges Rights { get; }

	/// <summary>Checks whether the actor holds the right an action needs.</summary>
	/// <param name="required">The right the action needs.</param>
	/// <returns><see langword="null" /> when it is held; otherwise what is missing.</returns>
	internal UserPrivileges? Require(UserPrivileges required)
	{
		return ScopedRights.Require(Rights, required);
	}

	private readonly ScoreSubmitter _submitter;

	internal ScopedScoreSubmitter(ScoreSubmitter submitter, StableConnection actor, UserPrivileges privileges)
	{
		Actor = actor;
		Rights = privileges;
		_submitter = submitter;
	}

	/// <summary>Validates and records the score the game client submitted.</summary>
	/// <param name="submission">The parsed submission.</param>
	/// <param name="beatmap">
	///     The beatmap the server knows for the submission, or <see langword="null" /> when the server does
	///     not have it.
	/// </param>
	/// <param name="client">The osu! client information sent with the submission.</param>
	/// <param name="replay">
	///     The seekable stream of the replay, positioned at its start and left open, or <see langword="null" /> for a
	///     failed play.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the submission.</param>
	/// <returns>
	///     Ok when the score was stored; otherwise Rejected with the rejection, or the privileges the client lacks.
	/// </returns>
	/// <remarks>
	///     Needs <see cref="UserPrivileges.Player" />. A score on a beatmap the server does not have is taken only
	///     when <see cref="IScoreBeatmapAcceptance" /> accepts it. Every accepted play adds to the play count and the total
	///     score; passed plays also add to the ranked score. The submission is checked against the player's latest
	///     recorded login. A submission whose checksum was already stored is refused. Only a passed play keeps its
	///     replay, and only when the replay is at least 24 bytes long.
	/// </remarks>
	public async Task<(Result<ScoreSubmissionOutcome> Result, ScoreRejectionReason? Rejection)> SubmitAsync(
		Submission submission,
		BeatmapChecksums? beatmap,
		SubmittedClient client,
		Stream? replay,
		CancellationToken cancellationToken = default)
	{
		if (Require(UserPrivileges.Player) is { } missing)
			return (Result<ScoreSubmissionOutcome>.Denied(missing), null);

		return await _submitter.SubmitAsync(Actor, submission, beatmap, client, replay, cancellationToken);
	}
}