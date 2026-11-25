using System.Reflection;
using System.Text.Json;
using Basil.Application.Servers.Ports;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Servers.Adapters;

internal sealed class GitHubReleases(IOptionsMonitor<UpdateOptions> options, IHttpClientFactory http) : IReleases
{
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

	public string Installed => Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "unknown";

	public async Task<string> LatestAsync(CancellationToken ct)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
		timeout.CancelAfter(Timeout);
		var path = new Uri(options.CurrentValue.Source).AbsolutePath.Trim('/');
		using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{path}/releases/latest");
		request.Headers.UserAgent.ParseAdd("Basil-update-check");
		request.Headers.Accept.ParseAdd("application/vnd.github+json");
		using var response = await http.CreateClient().SendAsync(request, timeout.Token);
		response.EnsureSuccessStatusCode();
		using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token),
			cancellationToken: timeout.Token);
		return body.RootElement.GetProperty("tag_name").GetString() ?? string.Empty;
	}
}
