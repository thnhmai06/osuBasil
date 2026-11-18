using System.Text;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Objects;
using Basil.Domain.Cryptography;

namespace Basil.Host.Cli.Commands;

internal static class CommandHelpers
{
	public const int MaxLimit = 100;

	public static readonly OptionDef[] Paging =
	[
		new("offset", "The index of the first item (default 0).", OptionKind.Int),
		new("limit", "The page size, 1 to 100 (default 25).", OptionKind.Int)
	];

	public static PageRequest Page(this CommandCall call)
	{
		var offset = call.Int("offset") ?? 0;
		var limit = call.Int("limit") ?? 25;
		if (offset < 0 || limit < 1 || limit > MaxLimit)
			throw CommandException.Invalid($"offset must be 0 or more and limit between 1 and {MaxLimit}.");

		return new PageRequest(offset, limit);
	}

	public static void PrintPage<T>(this CommandCall call, Page<T> page, PageRequest request) where T : class
	{
		call.Formatter.PrintTable(call.Output, [.. page.Items]);
		call.Output.Line(
			$"shown: {page.Items.Count} (offset {request.Offset}, limit {request.Limit}){(page.HasNext ? ", more follow" : "")}");
	}

	public static UserReference UserOf(this CommandCall call, string name = "id")
	{
		return UserReference.ById(call.Id(name));
	}

	public static Md5 Md5Of(string password)
	{
		return new Md5(Encoding.UTF8.GetBytes(password));
	}
}