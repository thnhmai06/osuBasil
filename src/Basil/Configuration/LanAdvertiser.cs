using System.Net;
using System.Net.Sockets;
using Makaretu.Dns;
using Microsoft.Extensions.Options;

namespace Basil.Configuration;

/// <summary>
///     Answers multicast DNS queries for the server's domain and the sub-domains it serves, so a client on the same
///     network finds it.
/// </summary>
internal sealed class LanAdvertiser(IOptions<ServerOptions> options, ILogger<LanAdvertiser> logger)
	: IHostedService, IDisposable
{
	/// <summary>The sub-domains the server serves; the <c>ppy.sh</c> hosts are never advertised.</summary>
	internal static readonly string[] Labels = ["c", "ce", "c4", "c5", "c6", "osu", "b", "a", "api", "assets"];

	private MulticastService? _service;

	public void Dispose()
	{
		_service?.Dispose();
		_service = null;
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		var server = options.Value;
		var multicast = server.Multicast;
		if (!multicast.Enabled) return Task.CompletedTask;

		Validate(multicast);
		var names = Names(server.Domain);
		var ttl = TimeSpan.FromSeconds(multicast.DnsTtl);
		try
		{
			_service = new MulticastService
			{
				UseIpv4 = !string.IsNullOrEmpty(multicast.IPv4),
				UseIpv6 = !string.IsNullOrEmpty(multicast.IPv6)
			};
			_service.QueryReceived += (_, query) =>
			{
				var response = query.Message.CreateResponse();
				foreach (var question in query.Message.Questions.Where(q =>
					         names.Contains(q.Name.ToString().TrimEnd('.'))))
				foreach (var address in MulticastService.GetIPAddresses())
					if (question.Type is DnsType.A or DnsType.ANY &&
					    address.AddressFamily == AddressFamily.InterNetwork)
						response.Answers.Add(new ARecord { Name = question.Name, Address = address, TTL = ttl });
					else if (question.Type is DnsType.AAAA or DnsType.ANY &&
					         address.AddressFamily == AddressFamily.InterNetworkV6)
						response.Answers.Add(new AAAARecord { Name = question.Name, Address = address, TTL = ttl });

				if (response.Answers.Count > 0) _service.SendAnswer(response);
			};
			_service.Start();
			logger.LogInformation("Answering multicast DNS queries for {Domain}", server.Domain);
		}
		catch (Exception exception)
		{
			logger.LogWarning(exception, "The domain is not advertised on the network:");
		}

		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		Dispose();
		return Task.CompletedTask;
	}

	/// <summary>Refuses a group address or port that the multicast DNS responder cannot use, instead of ignoring it.</summary>
	internal static void Validate(ServerOptions.MulticastSettings multicast)
	{
		if (!string.IsNullOrEmpty(multicast.IPv4) &&
		    !IPAddress.Parse(multicast.IPv4).Equals(IPAddress.Parse(ServerOptions.MulticastSettings.StandardIPv4)))
			throw new InvalidOperationException(
				$"Basil:Server:Multicast:IPv4 must be {ServerOptions.MulticastSettings.StandardIPv4} or empty; other group addresses are not supported.");
		if (!string.IsNullOrEmpty(multicast.IPv6) &&
		    !IPAddress.Parse(multicast.IPv6).Equals(IPAddress.Parse(ServerOptions.MulticastSettings.StandardIPv6)))
			throw new InvalidOperationException(
				$"Basil:Server:Multicast:IPv6 must be {ServerOptions.MulticastSettings.StandardIPv6} or empty; other group addresses are not supported.");
		if (multicast.Port != ServerOptions.MulticastSettings.StandardPort)
			throw new InvalidOperationException(
				$"Basil:Server:Multicast:Port must be {ServerOptions.MulticastSettings.StandardPort}; other ports are not supported.");
	}

	internal static HashSet<string> Names(string domain)
	{
		return new HashSet<string>([domain, .. Labels.Select(label => $"{label}.{domain}")],
			StringComparer.OrdinalIgnoreCase);
	}
}