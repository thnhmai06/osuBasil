using System.Text;
using Basil.Application.Common;
using Basil.Application.Users.Persistence;
using Basil.Domain.Common;
using Basil.Domain.Cryptography;
using Basil.Domain.Users;

namespace Basil.Host.Cli.Commands;

/// <summary>Pieces that several commands share: paging, finding an account and printing lists.</summary>
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
		call.Output.Line($"{page.Items.Count} of {page.Total} (offset {request.Offset}, limit {request.Limit})");
	}

	/// <summary>Finds an account by id; the console may see deleted accounts too only through their own commands.</summary>
	public static async Task<UserRecord?> FindUserAsync(this CommandCall call, string name = "id")
	{
		var record = await call.Get<IUserRepository>().GetAsync(call.Id(name), call.CancellationToken);
		return record is null || record.Value.DeletedAt is not null ? null : record;
	}

	/// <summary>Hashes a plain password the way the API does.</summary>
	public static Md5 Md5Of(string password)
	{
		return new Md5(Encoding.UTF8.GetBytes(password));
	}
}