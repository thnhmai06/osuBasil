using System.Net;
using Basil.Host;
using Basil.Host.Cli.Shutdown;
using Microsoft.Extensions.Options;

if (ShellRunner.IsCommand(args)) return await ShellRunner.RunAsync(args);

var app = HostApplication.Create(args);
if (Restarter.WasRestarted())
{
	var server = app.Services.GetRequiredService<IOptions<ServerOptions>>().Value;
	var address = IPAddress.Parse(server.Bind);
	await Restarter.WaitForPortsAsync(() => Restarter.CanBind(address, server.Ports.Https) && Restarter.CanBind(IPAddress.Any, server.Ports.Irc),
		TimeProvider.System, TimeSpan.FromSeconds(10));
}

// Resolved before the run: the services are disposed once the host has stopped.
var control = app.Services.GetRequiredService<ShutdownController>();
await app.RunAsync();
if (control is { RestartRequested: true } && !Restarter.UnderManager())
    Restarter.Start(args);

return control.ExitCode;
