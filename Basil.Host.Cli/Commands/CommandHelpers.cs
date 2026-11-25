using Basil.Domain.Users;
using Basil.Application.Users;
using System.Text;
using Basil.Application.Shared.Paging;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Host.Cli.Commands;

internal static class CommandHelpers
{
	public const int MaxLimit = 100;

	public static readonly OptionDef[] Paging =
	[
		new("offset", "The index of the first item (default 0).", OptionKind.Int),
		new("limit", "The page size, 1 to 100 (default 25).", OptionKind.Int)
	];

	extension(CommandCall call)
	{
		public PageRequest Page()
		{
			var offset = call.Int("offset") ?? 0;
			var limit = call.Int("limit") ?? 25;
			if (offset < 0 || limit < 1 || limit > MaxLimit)
				throw CommandException.Invalid($"offset must be 0 or more and limit between 1 and {MaxLimit}.");

			return new PageRequest(offset, limit);
		}

		public void PrintPage<T>(Page<T> page, PageRequest request) where T : class
		{
			call.Formatter.PrintTable(call.Output, [.. page.Items]);
			call.Output.Line(
				$"shown: {page.Items.Count} (offset {request.Offset}, limit {request.Limit}){(page.HasNext ? ", more follow" : "")}");
		}

		public int UserOf(string name = "id")
		{
			return call.Id(name);
		}
	}

	// A file the operator named: a missing or unreadable one is told in plain words.
	public static FileStream OpenFile(string path)
	{
		if (!File.Exists(path)) throw CommandException.Invalid($"There is no file at {path}.");

		try
		{
			return File.OpenRead(path);
		}
		catch (UnauthorizedAccessException)
		{
			throw CommandException.Invalid($"The file {path} cannot be read.");
		}
	}

	// Only a new password goes through here, so the length rule is checked once for every console path.
	public static Md5Hash Md5Of(string password)
	{
		if (!Password.IsAcceptable(password)) throw CommandException.Invalid(Password.TooShort);

		return new Md5Hash(Encoding.UTF8.GetBytes(password));
	}
}