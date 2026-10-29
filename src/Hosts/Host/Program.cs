using Basil.Host;
using Basil.Host.Cli.Shutdown;

if (ShellRunner.IsCommand(args)) return await ShellRunner.RunAsync(args);

var app = HostApplication.Create(args);
await app.RunAsync();
var control = app.Services.GetRequiredService<ShutdownController>();
if (control is { RestartRequested: true } && !Restarter.UnderManager())
    Restarter.Start(args);

return control.ExitCode;
