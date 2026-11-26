using System.Net;
using Basil.Tests.Shared.Clients.OsuWeb;
using Basil.Tests.Shared.Clients.Rest;
using Basil.Tests.Shared.Clients.Stable;
using Basil.Tests.Shared.Workload.Outcomes;
using Basil.Tests.Shared.Workload.World;
using NBomber.Contracts;

namespace Basil.Tests.Shared.Workload.Personas;

/// <summary>Exercises deliberate business-rule refusals and verifies each gets the expected refusal rather than an error.</summary>
public sealed class EdgeCasesPersona(WorkloadContext workload)
{
	/// <summary>Does nothing: every cycle signs itself out, so no session outlives it.</summary>
	public static Task ShutdownAsync()
	{
		return Task.CompletedTask;
	}

	/// <summary>
	///     Runs one cycle covering MatchJoinFail, duplicate score submission, second login within 30 s and
	///     forbidden/missing REST calls.
	/// </summary>
	public async Task RunCycleAsync(IScenarioContext context)
	{
		using var linked = workload.LinkToken(context);
		var ct = linked.Token;
		var random = SeededRandom.For(workload.Seed, context.ScenarioInfo.ScenarioName,
			context.ScenarioInfo.InstanceNumber, context.InvocationNumber);

		if (!workload.Accounts.TryAcquire(AccountRole.Player, out var lease))
		{
			await WorkloadContext.SafeDelayAsync(TimeSpan.FromSeconds(1), ct);
			return;
		}

		using (lease)
		{
			var identity = lease.Identity();
			var stable = new StableClient(workload.Endpoint, workload.Http, identity);
			var login = await workload.LoginStableAsync(stable, context, ct, lease: lease);
			if (login is not { Accepted: true }) return;

			var rest = new RestClient(workload.Endpoint, workload.Http);
			try
			{
				// 1. MatchJoinFail: attempt to join a non-existent room id.
				stable.Enqueue(ClientPackets.JoinMatch(999_999, "wrong-password"));
				var failed =
					await workload.PollUntilAsync<MatchJoinFailed>(stable, null, workload.WaitTimeout, context, ct);
				await StepExecution.RunAsync(Channels.Edge, context, workload.Outcomes, () =>
					Task.FromResult(failed is not null
						? StepOutcome.Refused("match_join_fail")
						: StepOutcome.Error("expected_match_join_fail")), ct);

				// 2. Duplicate score submission: submit the exact same play twice; second must be refused.
				var map = workload.PickBeatmap(random);
				var web = new OsuWebClient(workload.Endpoint, workload.Http, identity);
				var score = PlayedScore.Generate(random, map.Md5, true, map.Mode, 0,
					map.StoryboardMd5);
				var replay = SampleFiles.Replay(random);

				var first = await StepExecution.RunAsync(Channels.Submit, context, workload.Outcomes, async () =>
				{
					var reply = await web.SubmitScoreAsync(score, replay, ct);
					return OutcomeClassifier.FromScoreSubmission(reply.Status, reply.Body);
				}, ct);

				if (first.Outcome == Outcome.Ok)
					await StepExecution.RunAsync(Channels.Submit, context, workload.Outcomes, async () =>
					{
						var reply = await web.SubmitScoreAsync(score, replay, ct);
						var classified = OutcomeClassifier.FromScoreSubmission(reply.Status, reply.Body, true);
						return classified.Outcome == Outcome.Ok
							? StepOutcome.Error("expected_duplicate_refusal")
							: classified;
					}, ct);

				// 3. Second login within 30 s while the first session is active (AlreadyOnline; does not trip lockout).
				var duplicateClient = new StableClient(workload.Endpoint, workload.Http, identity);
				await StepExecution.RunAsync(Channels.Login, context, workload.Outcomes, async () =>
				{
					var reply = await duplicateClient.LoginAsync(ct);
					return reply.Status switch
					{
						HttpStatusCode.TooManyRequests => StepOutcome.Throttled(),
						HttpStatusCode.ServiceUnavailable => StepOutcome.Shed(),
						_ => reply is { Accepted: false, UserId: < 0 }
							? StepOutcome.Refused("already_online")
							: StepOutcome.Error("expected_already_online")
					};
				}, ct);

				// 4. REST: second login within 30 s -> 409, missing user -> 404, diagnostics without Developer -> 403.
				var restLogin = await StepExecution.RunAsync(Channels.Login, context, workload.Outcomes, async () =>
				{
					var reply = await rest.SignInAsync(lease.Account.Name, lease.Account.Password, ct);
					return OutcomeClassifier.FromRest(reply);
				}, ct);

				if (restLogin.Outcome == Outcome.Ok)
				{
					var secondRest = new RestClient(workload.Endpoint, workload.Http);
					await StepExecution.RunAsync(Channels.Login, context, workload.Outcomes, async () =>
					{
						var reply = await secondRest.SignInAsync(lease.Account.Name, lease.Account.Password, ct);
						return reply.Status == HttpStatusCode.Conflict
							? StepOutcome.Refused("rest_already_online")
							: OutcomeClassifier.FromRest(reply, true);
					}, ct);

					await StepExecution.RunAsync(Channels.Rest, context, workload.Outcomes, async () =>
					{
						var reply = await rest.GetAsync("/v1/users/99999999", ct);
						return Expected(reply.Status, HttpStatusCode.NotFound);
					}, ct);

					await StepExecution.RunAsync(Channels.Rest, context, workload.Outcomes, async () =>
					{
						using var stream = await rest.OpenStreamAsync("/v1/diagnostics", true, ct);
						return Expected(stream.Status, HttpStatusCode.Forbidden);
					}, ct);

					await workload.LogoutRestAsync(rest, context);
				}
			}
			finally
			{
				await workload.LogoutRestAsync(rest, context);
				await workload.LogoutStableAsync(stable, context);
			}
		}
	}

	// A refusal the edge case asks for; a busy or limiting server is counted as such, not as a wrong answer.
	private static StepOutcome Expected(HttpStatusCode got, HttpStatusCode wanted)
	{
		if (got == wanted) return StepOutcome.Refused($"{(int)wanted}");

		return got switch
		{
			HttpStatusCode.TooManyRequests => StepOutcome.Throttled(),
			HttpStatusCode.ServiceUnavailable => StepOutcome.Shed(),
			_ => StepOutcome.Error($"expected_{(int)wanted}_got_{(int)got}")
		};
	}
}
