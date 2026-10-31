using Basil.Host.Cli.Tests.Commands;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using System.Net.Sockets;
using Basil.Host.Cli.Commands;
using Basil.Host.Cli.Socket;
using Basil.Infrastructure.Common;
using Basil.Infrastructure.Common.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Basil.Host.Cli.Tests.Socket;

/// <summary>The local control socket of a running server, and the shell commands that use it.</summary>
[Collection("CLI host")]
public sealed class ControlSocketTests(CliFixture fixture)
{
	private string SocketPath => ControlSocket.PathIn(fixture.Host.Services.GetRequiredService<IOptions<StorageOptions>>().Value.DataDirectory);

	private static string ShortDirectory()
	{
		var path = Path.Combine(Path.GetTempPath(), "bs" + Guid.NewGuid().ToString("N")[..8]);
		Directory.CreateDirectory(path);
		return path;
	}

	private ControlServer NewServer(string path, CommandRunner? runner = null)
	{
		return new ControlServer(runner ?? fixture.Runner, path, NullLogger<ControlServer>.Instance);
	}

	[Fact]
	public async Task A_running_server_answers_a_command_over_its_socket_and_streams_the_output_with_the_exit_code()
	{
		using var connection = await ControlClient.ConnectAsync(SocketPath, TestContext.Current.CancellationToken);
		var output = new StringWriter();

		var code = await connection!.RunAsync(["users", "999999"], output, TestContext.Current.CancellationToken);

		Assert.Equal(1, code);
		Assert.Equal("error: notFound: No such user." + output.NewLine, output.ToString());
	}

	[Fact]
	public async Task A_command_through_the_socket_has_its_effect_in_the_server()
	{
		var name = CliFixture.Name("viasock");
		using var connection = await ControlClient.ConnectAsync(SocketPath, TestContext.Current.CancellationToken);

		var code = await connection!.RunAsync(["users", "create", "--username", name, "--password", "pw"], new StringWriter(), TestContext.Current.CancellationToken);

		Assert.Equal(0, code);
		Assert.NotNull(await fixture.UserAsync(name));
	}

	[Fact]
	public async Task Multi_line_output_is_framed_per_line_so_a_line_cannot_pass_for_the_exit_code()
	{
		var entry = CliFixture.Name("faqsock");
		var file = Path.GetTempFileName();
		await File.WriteAllTextAsync(file, "first\nx 99\nthird");
		await fixture.RunAsync($"faqs {entry} set --file {file}");
		using var connection = await ControlClient.ConnectAsync(SocketPath, TestContext.Current.CancellationToken);
		var output = new StringWriter();

		var code = await connection!.RunAsync(["faqs", entry], output, TestContext.Current.CancellationToken);
		File.Delete(file);

		Assert.Equal(0, code);
		Assert.Equal(["first", "x 99", "third"], output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
	}

	[Fact]
	public async Task A_garbage_request_is_answered_with_the_invalid_exit_code()
	{
		using var socket = await ControlSocket.TryConnectAsync(SocketPath, TestContext.Current.CancellationToken);
		await using var stream = new NetworkStream(socket!, false);
		await stream.WriteAsync("not json\n"u8.ToArray(), TestContext.Current.CancellationToken);
		using var reader = new StreamReader(stream);
		var output = new StringWriter();

		var code = await ControlSocket.ReadReplyAsync(reader, output, TestContext.Current.CancellationToken);

		Assert.Null(code);
	}

	[Fact]
	public async Task The_socket_is_readable_and_writable_by_its_owner_only()
	{
		Assert.True(File.Exists(SocketPath));
		if (OperatingSystem.IsWindows()) return;

		Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(SocketPath));
		await Task.CompletedTask;
	}

	[Fact]
	public async Task A_second_server_on_the_same_socket_refuses_to_start()
	{
		var other = NewServer(SocketPath);

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => other.StartAsync(TestContext.Current.CancellationToken));

		Assert.Contains("Another Basil server is running", exception.Message);
	}

	[Fact]
	public async Task A_leftover_socket_file_of_a_crashed_server_is_replaced()
	{
		var directory = ShortDirectory();
		var path = ControlSocket.PathIn(directory);
		// A crashed server leaves the file behind with nobody listening on it.
		await File.WriteAllTextAsync(path, string.Empty, TestContext.Current.CancellationToken);
		Assert.True(File.Exists(path));
		var server = NewServer(path);
		try
		{
			await server.StartAsync(TestContext.Current.CancellationToken);

			using var connection = await ControlClient.ConnectAsync(path, TestContext.Current.CancellationToken);
			Assert.NotNull(connection);
		}
		finally
		{
			await server.StopAsync(CancellationToken.None);
			Directory.Delete(directory, true);
		}
	}

	[Fact]
	public async Task Stopping_the_server_removes_the_socket_and_a_client_then_finds_nobody()
	{
		var directory = ShortDirectory();
		var path = ControlSocket.PathIn(directory);
		var server = NewServer(path);
		await server.StartAsync(TestContext.Current.CancellationToken);

		await server.StopAsync(CancellationToken.None);

		Assert.False(File.Exists(path));
		Assert.Null(await ControlClient.ConnectAsync(path, TestContext.Current.CancellationToken));
		Directory.Delete(directory, true);
	}

	[Fact]
	public async Task A_path_too_long_for_a_unix_socket_runs_the_server_without_one()
	{
		var directory = Path.Combine(Path.GetTempPath(), new string('d', 120));
		Directory.CreateDirectory(directory);
		var path = ControlSocket.PathIn(directory);
		var server = NewServer(path);
		try
		{
			await server.StartAsync(TestContext.Current.CancellationToken);

			Assert.False(File.Exists(path));
			Assert.Null(await ControlClient.ConnectAsync(path, TestContext.Current.CancellationToken));
		}
		finally
		{
			await server.StopAsync(CancellationToken.None);
			Directory.Delete(directory, true);
		}
	}

	[Fact]
	public async Task The_shell_forwards_to_the_running_server_and_prints_its_output()
	{
		var output = new StringWriter();
		var dataDirectory = fixture.Host.Services.GetRequiredService<IOptions<StorageOptions>>().Value.DataDirectory;

		var code = await ShellRunner.RunAsync(["users", "999999"],
			b => b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Basil:Storage:DataDirectory"] = dataDirectory }),
			output, new StringReader(string.Empty), TestContext.Current.CancellationToken);

		Assert.Equal(1, code);
		Assert.Contains("notFound", output.ToString());
	}

	[Fact]
	public async Task The_shell_asks_in_the_calling_terminal_before_it_shuts_a_server_down_and_adds_now()
	{
		var directory = ShortDirectory();
		var path = ControlSocket.PathIn(directory);
		var control = new RecordingControl();
		var runner = new CommandRunner(fixture.Host.Services.GetRequiredService<CommandCatalog>(), new WithControl(fixture.Host.Services, control),
			new ViewFormatter(TimeZoneInfo.Utc), NullLogger<CommandRunner>.Instance);
		var server = NewServer(path, runner);
		await server.StartAsync(TestContext.Current.CancellationToken);
		try
		{
			async Task<(int Code, string Text)> Shell(string answer)
			{
				var output = new StringWriter();
				var code = await ShellRunner.RunAsync(["shutdown"],
					b => b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Basil:Storage:DataDirectory"] = directory }),
					output, new StringReader(answer + Environment.NewLine), TestContext.Current.CancellationToken);
				return (code, output.ToString());
			}

			var (declined, declinedText) = await Shell("n");
			Assert.Equal(4, declined);
			Assert.Contains("Really shut down the server? (y/N)", declinedText);
			Assert.Empty(control.Requests);

			var (accepted, _) = await Shell("y");
			Assert.Equal(0, accepted);
			Assert.Equal([false], control.Requests);
		}
		finally
		{
			await server.StopAsync(CancellationToken.None);
			Directory.Delete(directory, true);
		}
	}

	[Fact]
	public async Task With_now_the_shell_asks_nothing()
	{
		var directory = ShortDirectory();
		var path = ControlSocket.PathIn(directory);
		var control = new RecordingControl();
		var runner = new CommandRunner(fixture.Host.Services.GetRequiredService<CommandCatalog>(), new WithControl(fixture.Host.Services, control),
			new ViewFormatter(TimeZoneInfo.Utc), NullLogger<CommandRunner>.Instance);
		var server = NewServer(path, runner);
		await server.StartAsync(TestContext.Current.CancellationToken);
		try
		{
			var code = await ShellRunner.RunAsync(["restart", "--now"],
				b => b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Basil:Storage:DataDirectory"] = directory }),
				new StringWriter(), new StringReader(string.Empty), TestContext.Current.CancellationToken);

			Assert.Equal(0, code);
			Assert.Equal([true], control.Requests);
		}
		finally
		{
			await server.StopAsync(CancellationToken.None);
			Directory.Delete(directory, true);
		}
	}

	[Fact]
	public async Task Without_a_server_a_command_that_needs_it_fails_with_code3_and_shutdown_asks_nothing()
	{
		var directory = ShortDirectory();
		Action<WebApplicationBuilder> configure = b => b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Basil:Storage:DataDirectory"] = directory });
		try
		{
			var rooms = new StringWriter();
			var shutdown = new StringWriter();

			var roomsCode = await ShellRunner.RunAsync(["rooms", "list"], configure, rooms, new StringReader(string.Empty), TestContext.Current.CancellationToken);
			var shutdownCode = await ShellRunner.RunAsync(["shutdown"], configure, shutdown, new StringReader("y"), TestContext.Current.CancellationToken);

			Assert.Equal(3, roomsCode);
			Assert.Contains("serverNotRunning", rooms.ToString());
			Assert.Equal(3, shutdownCode);
			Assert.DoesNotContain("Really", shutdown.ToString());
		}
		finally
		{
			Directory.Delete(directory, true);
		}
	}

	[Fact]
	public async Task Without_a_server_help_runs_without_touching_the_storage()
	{
		var directory = ShortDirectory();
		try
		{
			var output = new StringWriter();

			var code = await ShellRunner.RunAsync(["help", "users"],
				b => b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Basil:Storage:DataDirectory"] = directory, ["Basil:Database:ConnectionString"] = "Host=invalid;Timeout=1" }),
				output, new StringReader(string.Empty), TestContext.Current.CancellationToken);

			Assert.Equal(0, code);
			Assert.Contains("users create", output.ToString());
		}
		finally
		{
			Directory.Delete(directory, true);
		}
	}

	[Fact]
	public async Task Without_a_server_an_offline_command_stores_its_change_before_it_returns()
	{
		var name = "basil-cli-offline-" + Guid.NewGuid().ToString("N");
		var connection = await fixture.Database.CreateDatabaseAsync(name);
		var root = Path.Combine(Path.GetTempPath(), name);
		Dictionary<string, string?> Settings() => new()
		{
			["Basil:Database:ConnectionString"] = connection,
			["Basil:Storage:DataDirectory"] = Path.Combine(root, "Data"),
			["Basil:Server:Domain"] = BasilHost.DomainName,
			["Basil:Server:Ports:Irc"] = "0",
			["Basil:Bot:Replies:Path"] = Path.Combine(root, "Templates", "replies.json")
		};
		try
		{
			var output = new StringWriter();

			var created = await ShellRunner.RunAsync(["users", "create", "--username", "Offline1", "--password", "s3cret"],
				b => b.Configuration.AddInMemoryCollection(Settings()), output, new StringReader(string.Empty), TestContext.Current.CancellationToken);
			var shown = new StringWriter();
			var listed = await ShellRunner.RunAsync(["users", "list", "--query", "Offline1"],
				b => b.Configuration.AddInMemoryCollection(Settings()), shown, new StringReader(string.Empty), TestContext.Current.CancellationToken);

			Assert.Equal(0, created);
			Assert.Matches(@"Created Offline1 with id \d+\.", output.ToString());
			Assert.Equal(0, listed);
			Assert.Contains("Offline1", shown.ToString());
		}
		finally
		{
			await fixture.Database.DropDatabaseAsync(name);
			if (Directory.Exists(root)) Directory.Delete(root, true);
		}
	}
}
