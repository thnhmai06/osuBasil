using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace LoadHarness;

/// <summary>An API caller: signs in once on <c>api.</c>, then reads users, rooms and scores.</summary>
internal sealed class ApiClient(Context context, Account account)
{
	private const string Kind = "api";

	public async Task RunAsync(CancellationToken ct)
	{
		var session = await SignInAsync(ct);
		if (session is not var (token, userId)) return;

		try
		{
			string[] reads = [$"/v1/users/{userId}", "/v1/rooms", $"/v1/scores?user={userId}", "/v1/scores", "/v1/users/1"];
			while (!ct.IsCancellationRequested)
			{
				await Context.Pause(context.Options.ApiInterval, ct);
				await GetAsync(token, reads[Random.Shared.Next(reads.Length)], ct);
			}
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			await SignOutAsync(token);
		}
	}

	private async Task<(string Token, int UserId)?> SignInAsync(CancellationToken ct)
	{
		var started = Stopwatch.GetTimestamp();
		try
		{
			using var response = await context.Http.PostAsJsonAsync(context.Url("api", "/v1/auth/login"),
				new { username = account.Name, password = account.Password }, ct);
			if (!response.IsSuccessStatusCode)
			{
				context.Recorder.Record("api-login", Stopwatch.GetElapsedTime(started), $"HTTP {(int)response.StatusCode}", true);
				return null;
			}

			using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
			var data = document.RootElement.GetProperty("data");
			context.Recorder.Record("api-login", Stopwatch.GetElapsedTime(started), null, true);
			return (data.GetProperty("token").GetString()!, data.GetProperty("userId").GetInt32());
		}
		catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
		{
			context.Recorder.Record("api-login", Stopwatch.GetElapsedTime(started), Context.Reason(exception), true);
			return null;
		}
		catch (OperationCanceledException)
		{
			return null;
		}
	}

	private async Task GetAsync(string token, string path, CancellationToken ct)
	{
		var started = Stopwatch.GetTimestamp();
		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, context.Url("api", path));
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
			using var response = await context.Http.SendAsync(request, ct);
			await response.Content.ReadAsByteArrayAsync(ct);
			context.Recorder.Record(Kind, Stopwatch.GetElapsedTime(started),
				response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}");
		}
		catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
		{
			context.Recorder.Record(Kind, Stopwatch.GetElapsedTime(started), Context.Reason(exception));
		}
	}

	private async Task SignOutAsync(string token)
	{
		try
		{
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
			using var request = new HttpRequestMessage(HttpMethod.Post, context.Url("api", "/v1/auth/logout"));
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
			(await context.Http.SendAsync(request, timeout.Token)).Dispose();
		}
		catch
		{
			// The session ends by its idle limit if this fails.
		}
	}
}
