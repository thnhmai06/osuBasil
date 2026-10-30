using Makaretu.Dns;
using Microsoft.Extensions.Options;

namespace Basil.Host.Configuration;

/// <summary>Answers multicast DNS queries for the server's domain and the sub-domains it serves, so a client on the same network finds it.</summary>
internal sealed class LanAdvertiser(IOptions<ServerOptions> options, ILogger<LanAdvertiser> logger) : IHostedService, IDisposable
{
	/// <summary>The sub-domains the server serves; the <c>ppy.sh</c> hosts are never advertised.</summary>
	internal static readonly string[] Labels = ["c", "ce", "c4", "c5", "c6", "osu", "b", "a", "api", "assets"];

	private MulticastService? _service;

	public Task StartAsync(CancellationToken cancellationToken)
	{
		var server = options.Value;
		if (!server.LanAdvertise) return Task.CompletedTask;

		var names = Names(server.Domain);
		try
		{
			_service = new MulticastService();
			_service.QueryReceived += (_, query) =>
			{
				var response = query.Message.CreateResponse();
				foreach (var question in query.Message.Questions.Where(q => names.Contains(q.Name.ToString().TrimEnd('.'))))
					foreach (var address in MulticastService.GetIPAddresses())
						if (question.Type is DnsType.A or DnsType.ANY && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
							response.Answers.Add(new ARecord { Name = question.Name, Address = address });
						else if (question.Type is DnsType.AAAA or DnsType.ANY && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
							response.Answers.Add(new AAAARecord { Name = question.Name, Address = address });

				if (response.Answers.Count > 0) _service.SendAnswer(response);
			};
			_service.Start();
			logger.LogInformation("Answering multicast DNS queries for {Domain}", server.Domain);
		}
		catch (Exception exception)
		{
			logger.LogWarning("The domain is not advertised on the network: {Reason}", exception.Message);
		}

		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		Dispose();
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		_service?.Dispose();
		_service = null;
	}

	internal static HashSet<string> Names(string domain)
	{
		return new HashSet<string>([domain, .. Labels.Select(label => $"{label}.{domain}")], StringComparer.OrdinalIgnoreCase);
	}
}
