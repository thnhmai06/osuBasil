using System.Text;
using Basil.Application.Shared;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Persistence;
using Basil.Domain.Cryptography;
using Basil.Domain.Shared;
using Basil.Domain.Users;

namespace Basil.Host.Cli.Commands;

/// <summary>Pieces that several commands share: paging, naming an account and printing lists.</summary>
internal static class CommandHelpers
{
	/// <summary>The most items a page may hold, as in the API.</summary>
	public const int MaxLimit = 100;

	/// <summary>The options of every list.</summary>
	public static readonly OptionDef[] Paging =
	[
		new("offset", "The index of the first item (default 0).", OptionKind.Int),
		new("limit", "The page size, 1 to 100 (default 25).", OptionKind.Int)
	];

	/// <summary>Reads <c>--offset</c> and <c>--limit</c>.</summary>
	public static PageRequest Page(this CommandCall call)
	{
		var offset = call.Int("offset") ?? 0;
		var limit = call.Int("limit") ?? 25;
		if (offset < 0 || limit < 1 || limit > MaxLimit)
			throw CommandException.Invalid($"offset must be 0 or more and limit between 1 and {MaxLimit}.");

		return new PageRequest(offset, limit);
	}

	/// <summary>Prints a page of views as a table with its position.</summary>
	public static void PrintPage<T>(this CommandCall call, Page<T> page, PageRequest request) where T : class
	{
		call.Formatter.PrintTable(call.Output, [.. page.Items]);
		call.Output.Line(
			$"shown: {page.Items.Count} (offset {request.Offset}, limit {request.Limit}){(page.HasNext ? ", more follow" : "")}");
	}

	/// <summary>Reads the account a command names by <c>--id</c>.</summary>
	public static UserReference UserOf(this CommandCall call, string name = "id")
	{
		return UserReference.ById(call.Id(name));
	}

	/// <summary>Hashes a plain password the way the API does.</summary>
	public static Md5 Md5Of(string password)
	{
		return new Md5(Encoding.UTF8.GetBytes(password));
	}
}