namespace Basil.Host;

/// <summary>
///     The commands that run against the storage without starting the server, such as creating the first administrator.
///     They start only the storage (the database is migrated, the stored data loaded), never recover the running state,
///     and
///     wait for every write to be stored before they end. The commands themselves are the console commands.
/// </summary>
public static class OfflineRunner
{
	/// <summary>Tells whether the command line names a console command.</summary>
	/// <param name="args">The command-line arguments.</param>
	/// <returns><see langword="true" /> when the first argument is the name of a console command.</returns>
	public static bool Handles(string[] args)
	{
		return ShellRunner.IsCommand(args);
	}

	/// <summary>Runs a console command from the shell: in the running server, or offline when it does not need the server.</summary>
	/// <param name="args">The command-line arguments, for example <c>users create --username a --password b</c>.</param>
	/// <param name="configure">Changes the builder before it is built; the tests use it to point at their storage.</param>
	/// <returns>The exit code: 0 when the command did its work.</returns>
	public static Task<int> RunAsync(string[] args, Action<WebApplicationBuilder>? configure = null)
	{
		return ShellRunner.RunAsync(args, configure);
	}
}