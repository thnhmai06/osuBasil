using Basil.Host;

if (OfflineRunner.Handles(args)) return await OfflineRunner.RunAsync(args);

await HostApplication.Create(args).RunAsync();
return 0;
