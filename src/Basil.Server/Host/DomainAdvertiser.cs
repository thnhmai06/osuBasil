using System.Net.Sockets;
using Basil.Server.Shared.Configuration;
using Basil.Server.Shared.Http;
using Makaretu.Dns;
using Microsoft.Extensions.Options;

namespace Basil.Server.Host;

/// <summary>
///     Answers multicast DNS queries for the configured domain, so clients on the same network
///     resolve it to this machine without a hosts entry.
/// </summary>
/// <remarks>
///     Only the configured domain and the subdomains this server actually serves are answered. The
///     server also serves the equivalent <c>ppy.sh</c> hosts, but claiming those on a shared network
///     would answer for traffic that is not this server's, so they are never advertised.
///
///     Multicast DNS is consulted by an operating system only for names it treats as link-local,
///     which in practice means names ending in <c>.local</c>. A domain outside that suffix is still
///     answered here, but most clients will never ask, and those deployments need the hosts file.
/// </remarks>
internal sealed class DomainAdvertiser(
	IOptions<ServerOptions> serverOptions,
	ILogger<DomainAdvertiser> logger) : IHostedService, IDisposable
{
	private readonly string[] _names = [.. BanchoHostGroups.HostNamesFor(serverOptions.Value.Domain)];
	private MulticastService? _mdns;

	/// <inheritdoc />
	public void Dispose()
	{
		_mdns?.Dispose();
	}

	/// <inheritdoc />
	public Task StartAsync(CancellationToken cancellationToken)
	{
		if (!serverOptions.Value.AdvertiseDomain)
		{
			logger.LogInformation("Not advertising {Domain} on the local network: turned off in settings",
				serverOptions.Value.Domain);
			return Task.CompletedTask;
		}

		_mdns = new MulticastService();
		_mdns.QueryReceived += OnQueryReceived;
		_mdns.Start();

		logger.LogInformation("Advertising {Domain} and {Count} subdomains on the local network",
			serverOptions.Value.Domain, _names.Length - 1);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task StopAsync(CancellationToken cancellationToken)
	{
		if (_mdns is null) return Task.CompletedTask;

		_mdns.QueryReceived -= OnQueryReceived;
		_mdns.Stop();
		return Task.CompletedTask;
	}

	private void OnQueryReceived(object? sender, MessageEventArgs e)
	{
		var service = _mdns;
		if (service is null) return;

		var answered = e.Message.CreateResponse();

		foreach (var question in e.Message.Questions)
		{
			var name = question.Name.ToString().TrimEnd('.');
			if (!_names.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;

			foreach (var address in MulticastService.GetIPAddresses())
			{
				if (question.Type is DnsType.A && address.AddressFamily is AddressFamily.InterNetwork)
					answered.Answers.Add(new ARecord { Name = question.Name, Address = address });
				else if (question.Type is DnsType.AAAA && address.AddressFamily is AddressFamily.InterNetworkV6)
					answered.Answers.Add(new AAAARecord { Name = question.Name, Address = address });
				else if (question.Type is DnsType.ANY)
					answered.Answers.Add(address.AddressFamily is AddressFamily.InterNetworkV6
						? new AAAARecord { Name = question.Name, Address = address }
						: new ARecord { Name = question.Name, Address = address });
			}
		}

		if (answered.Answers.Count == 0) return;

		try
		{
			service.SendAnswer(answered);
		}
		catch (Exception exception)
		{
			// A send can fail when an interface disappears between the query and the answer. The
			// querier simply retries or falls back, so this must not take the server down.
			logger.LogDebug(exception, "Could not answer a multicast DNS query");
		}
	}
}
