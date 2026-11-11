using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Basil.Application.Shared;
using Basil.Application.Shared.Settings;
using Basil.Application.Content.Messages;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Diagnostics.Messages;
using Basil.Application.Servers.Messages;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Persistence;
using Basil.Domain.Content;
using Basil.Domain.Users;
using Basil.Host.Shared;
using Basil.Host.Shared.Views;
using Mediator;
using Basil.Application.Servers.Objects;

namespace Basil.Host.Cli.Commands;

/// <summary>
///     The <c>menu</c>, <c>faqs</c>, <c>settings</c>, <c>announce</c>, <c>screenshots</c> and <c>diagnostics</c>
///     commands.
/// </summary>
internal static class ContentCommands
{
	private static readonly OptionDef[] BannerFields =
	[
		new("image", "The path of the image.", OptionKind.Path, true),
		new("url", "Where the banner leads.", Required: true),
		new("begins", "An ISO 8601 time the banner starts."), new("expires", "An ISO 8601 time the banner ends.")
	];

	/// <summary>Creates the commands of this group.</summary>
	public static IEnumerable<CommandDef> Create()
	{
		yield return new CommandDef
		{
			Pattern = "menu banners list", Route = "GET /v1/menu/banners", Offline = true,
			Summary = "Lists the main-menu banners.",
			Run = ListBannersAsync
		};
		yield return new CommandDef
		{
			Pattern = "menu banners create", Route = "POST /v1/menu/banners", Offline = true,
			Summary = "Adds a main-menu banner.",
			Options = BannerFields, Run = AddBannerAsync
		};
		yield return new CommandDef
		{
			Pattern = "menu banners <image> delete", Route = "DELETE /v1/menu/banners/{image}", Offline = true,
			Summary = "Deletes a main-menu banner.", Run = DeleteBannerAsync
		};
		yield return new CommandDef
		{
			Pattern = "menu seasonals list", Route = "GET /v1/menu/seasonals", Offline = true,
			Summary = "Lists the seasonal backgrounds.", Run = ListSeasonalsAsync
		};
		yield return new CommandDef
		{
			Pattern = "menu seasonals create", Route = "POST /v1/menu/seasonals", Offline = true,
			Summary = "Adds a seasonal background.",
			Options = [new OptionDef("image", "The path of the image.", OptionKind.Path, true)],
			Run = AddSeasonalAsync
		};
		yield return new CommandDef
		{
			Pattern = "menu seasonals <image> delete", Route = "DELETE /v1/menu/seasonals/{image}", Offline = true,
			Summary = "Deletes a seasonal background.", Run = DeleteSeasonalAsync
		};
		yield return new CommandDef
		{
			Pattern = "menu icon", Route = "GET /v1/menu/icon", Offline = true, Summary = "Shows the main-menu icon.",
			Run = ShowIconAsync
		};
		yield return new CommandDef
		{
			Pattern = "menu icon set", Route = "PUT /v1/menu/icon", Offline = true,
			Summary = "Sets the main-menu icon.",
			Options =
			[
				new OptionDef("image", "The path of the image.", OptionKind.Path),
				new OptionDef("url", "Where the icon leads.")
			],
			Run = SetIconAsync
		};
		yield return new CommandDef
		{
			Pattern = "menu icon delete", Route = "DELETE /v1/menu/icon", Offline = true,
			Summary = "Removes the main-menu icon.",
			Run = async call => call.Report(
				await SendAsync(call, actor => new ClearMenuIcon(actor)),
				"Icon removed.")
		};

		yield return new CommandDef
		{
			Pattern = "faqs list", Route = "GET /v1/faqs", Offline = true, Summary = "Lists the FAQ entries.",
			Run = ListFaqsAsync
		};
		yield return new CommandDef
		{
			Pattern = "faqs <entry>", Route = "GET /v1/faqs/{entry}", Offline = true, Summary = "Shows a FAQ entry.",
			Run = ShowFaqAsync
		};
		yield return new CommandDef
		{
			Pattern = "faqs <entry> set", Route = "PUT /v1/faqs/{entry}", Offline = true,
			Summary = "Creates or replaces a FAQ entry from a text file.",
			Options = [new OptionDef("file", "The path of the text file.", OptionKind.Path, true)], Run = SetFaqAsync
		};
		yield return new CommandDef
		{
			Pattern = "faqs <entry> delete", Route = "DELETE /v1/faqs/{entry}", Offline = true,
			Summary = "Deletes a FAQ entry.",
			Run = DeleteFaqAsync
		};

		yield return new CommandDef
		{
			Pattern = "settings", Route = "GET /v1/settings", Offline = true,
			Summary = "Shows every settings category.", Run = ShowSettingsAsync
		};
		yield return new CommandDef
		{
			Pattern = "settings <category>", Route = "GET /v1/settings/{category}", Offline = true,
			Summary = "Shows one settings category.", Run = ShowCategoryAsync
		};
		yield return new CommandDef
		{
			Pattern = "settings <category> set", Route = "PATCH /v1/settings/{category}", Offline = true,
			OpenOptions = true,
			Summary =
				"Changes fields of one settings category; a nested field is written with dots, as in --chat.count 5.",
			Run = SetSettingsAsync
		};

		yield return new CommandDef
		{
			Pattern = "announce create", Route = "POST /v1/announce",
			Summary = "Announces to some users or to everyone online.",
			Options =
			[
				new OptionDef("text", "The text.", Required: true),
				new OptionDef("to", "User ids separated by commas; none means everyone.", OptionKind.List)
			],
			Run = AnnounceAsync
		};

		yield return new CommandDef
		{
			Pattern = "screenshots list", Route = "GET /v1/screenshots", Offline = true,
			Summary = "Lists the stored screenshots.",
			Options = CommandHelpers.Paging, Run = ListScreenshotsAsync
		};
		yield return new CommandDef
		{
			Pattern = "screenshots <id> delete", Route = "DELETE /v1/screenshots/{id}", Offline = true,
			Summary = "Deletes a screenshot.", Run = DeleteScreenshotAsync
		};

		const string diagnosticsRoute = "POST /v1/diagnostics/{category}/{**action}";
		yield return new CommandDef
		{
			Pattern = "diagnostics gc collect", Route = diagnosticsRoute, Summary = "Collects the garbage now.",
			Run = async call => call.Report(await SendAsync(call, actor => new CollectGarbage(actor)),
				"Garbage collected.")
		};
		yield return new CommandDef
		{
			Pattern = "diagnostics caches clear", Route = diagnosticsRoute, Offline = true,
			Summary = "Empties the cache of derived files.",
			Run = async call =>
			{
				var (result, deleted) = await SendAsync(call, actor => new ClearCaches(actor));
				return call.Report(result, $"Cache cleared: {deleted} files deleted.");
			}
		};
		yield return new CommandDef
		{
			Pattern = "diagnostics exceptions reset", Route = diagnosticsRoute,
			Summary = "Forgets the counted exceptions.",
			Run = async call => call.Report(await SendAsync(call, actor => new ResetExceptions(actor)),
				"Exceptions reset.")
		};
	}

	private static async Task<T> SendAsync<T>(CommandCall call, Func<Connection, IRequest<T>> message)
	{
		return await call.Get<ISender>().Send(message(await call.ByAsync()), call.CancellationToken);
	}

	private static async Task<int> ListBannersAsync(CommandCall call)
	{
		var now = call.Get<TimeProvider>().GetUtcNow();
		var banners = await call.Get<ISender>().Send(new ListBanners(), call.CancellationToken);
		call.Formatter.PrintTable(call.Output, [.. banners.Select(object (banner) => BannerView.Of(banner, now))]);
		return ExitCodes.Success;
	}

	private static bool TryTime(string? text, out DateTimeOffset? value)
	{
		value = null;
		if (string.IsNullOrEmpty(text)) return true;

		var ok = DateTimeOffset.TryParse(text, null, DateTimeStyles.RoundtripKind, out var parsed);
		value = ok ? parsed : null;
		return ok;
	}

	private static async Task<int> AddBannerAsync(CommandCall call)
	{
		if (!Uri.TryCreate(call.Required("url"), UriKind.Absolute, out var url))
			throw CommandException.Invalid("--url is not valid.");
		if (!TryTime(call.Text("begins"), out var begins) || !TryTime(call.Text("expires"), out var expires))
			throw CommandException.Invalid("--begins and --expires must be ISO 8601 times.");

		var path = call.Required("image");
		await using var stream = File.OpenRead(path);
		var (result, banner) = await SendAsync(call,
			actor => new AddMenuBanner(actor, Path.GetFileName(path), stream, url, begins, expires));
		if (result != ServerOutcome.Ok) return call.Refuse(result);

		call.Output.Ok($"Added banner {Path.GetFileName(banner!.Image.Value)}.");
		return ExitCodes.Success;
	}

	private static async Task<int> DeleteBannerAsync(CommandCall call)
	{
		var image = call.Word("image");
		var banner = (await call.Get<ISender>().Send(new ListBanners(), call.CancellationToken))
			.FirstOrDefault(b => Path.GetFileName(b.Image.Value) == image);
		return banner is null
			? call.NotFound("banner")
			: call.Report(await SendAsync(call, actor => new DeleteMenuBanner(actor, banner)),
				"Banner deleted.");
	}

	private static async Task<int> ListSeasonalsAsync(CommandCall call)
	{
		foreach (var reference in await call.Get<ISender>().Send(new ListSeasonals(), call.CancellationToken))
			call.Output.Line(Path.GetFileName(reference.Value));
		return ExitCodes.Success;
	}

	private static async Task<int> AddSeasonalAsync(CommandCall call)
	{
		var path = call.Required("image");
		await using var stream = File.OpenRead(path);
		var (result, image) =
			await SendAsync(call, actor => new AddMenuSeasonal(actor, Path.GetFileName(path), stream));
		if (result != ServerOutcome.Ok) return call.Refuse(result);

		call.Output.Ok($"Added background {Path.GetFileName(image!.Value.Value)}.");
		return ExitCodes.Success;
	}

	private static async Task<int> DeleteSeasonalAsync(CommandCall call)
	{
		var image = call.Word("image");
		var reference = (await call.Get<ISender>().Send(new ListSeasonals(), call.CancellationToken))
			.Cast<ContentReference?>()
			.FirstOrDefault(r => Path.GetFileName(r!.Value.Value) == image);
		return reference is null
			? call.NotFound("background")
			: call.Report(
				await SendAsync(call, actor => new DeleteMenuSeasonal(actor, reference.Value)),
				"Background deleted.");
	}

	private static async Task<int> ShowIconAsync(CommandCall call)
	{
		var icon = (await call.Get<ISender>().Send(new ReadSettings(), call.CancellationToken)).MenuIcon;
		call.Formatter.Print(call.Output,
			new IconView(icon.Image is { } image ? Path.GetFileName(image.Value) : null, icon.Url?.AbsoluteUri));
		return ExitCodes.Success;
	}

	private static async Task<int> SetIconAsync(CommandCall call)
	{
		Uri? url = null;
		if (call.Text("url") is { Length: > 0 } text && !Uri.TryCreate(text, UriKind.Absolute, out url))
			throw CommandException.Invalid("--url is not valid.");

		var path = call.Text("image");
		await using var stream = path is null ? null : File.OpenRead(path);
		var result = await SendAsync(call,
			actor => new SetMenuIcon(actor, path is null ? null : Path.GetFileName(path), stream, url));
		return call.Report(result, "Icon set.");
	}

	private static async Task<int> ListFaqsAsync(CommandCall call)
	{
		foreach (var entry in await call.Get<ISender>().Send(new ListFaqs(), call.CancellationToken))
			call.Output.Line(entry);
		return ExitCodes.Success;
	}

	private static async Task<int> ShowFaqAsync(CommandCall call)
	{
		var entry = call.Word("entry");
		if (!FaqEntryName.IsValid(entry) ||
		    await call.Get<ISender>().Send(new ReadFaq(entry), call.CancellationToken) is not { } text)
			return call.NotFound("entry");

		call.Output.Line(text);
		return ExitCodes.Success;
	}

	private static async Task<int> SetFaqAsync(CommandCall call)
	{
		var entry = call.Word("entry");
		if (!FaqEntryName.IsValid(entry)) throw CommandException.Invalid("The entry name is not valid.");

		var text = await File.ReadAllTextAsync(call.Required("file"), call.CancellationToken);
		return call.Report(
			await SendAsync(call, actor => new SaveFaq(actor, entry, text)),
			"Entry saved.");
	}

	private static async Task<int> DeleteFaqAsync(CommandCall call)
	{
		var entry = call.Word("entry");
		return FaqEntryName.IsValid(entry)
			? call.Report(await SendAsync(call, actor => new DeleteFaq(actor, entry)), "Entry deleted.")
			: call.NotFound("entry");
	}

	/// <summary>Prints a JSON node as dotted <c>label: value</c> lines.</summary>
	private static void Flatten(List<(string, string)> lines, string prefix, JsonNode? node)
	{
		switch (node)
		{
			case JsonObject obj:
				foreach (var (key, value) in obj) Flatten(lines, prefix.Length == 0 ? key : $"{prefix}.{key}", value);
				break;
			case JsonArray array:
				lines.Add((prefix,
					array.Count == 0 ? "-" : string.Join(", ", array.Select(item => item?.ToJsonString().Trim('"')))));
				break;
			case null:
				lines.Add((prefix, "-"));
				break;
			default:
				lines.Add((prefix, node.ToJsonString().Trim('"')));
				break;
		}
	}

	private static async Task<int> ShowSettingsAsync(CommandCall call)
	{
		var (result, settings) = await SendAsync(call, actor => new GetSettings(actor));
		if (result != ServerOutcome.Ok) return call.Refuse(result);

		var lines = new List<(string, string)>();
		foreach (var (name, value) in SettingsCategories.GetAll(settings!))
			Flatten(lines, name, JsonSerializer.SerializeToNode(value, ApiJson.Options));
		call.Output.Fields(lines);
		return ExitCodes.Success;
	}

	private static async Task<int> ShowCategoryAsync(CommandCall call)
	{
		var category = call.Word("category");
		if (!SettingsCategories.Exists(category)) return call.NotFound("settings category");

		var (result, settings) = await SendAsync(call, actor => new GetSettings(actor));
		if (result != ServerOutcome.Ok) return call.Refuse(result);

		var lines = new List<(string, string)>();
		Flatten(lines, string.Empty,
			JsonSerializer.SerializeToNode(SettingsCategories.Get(category, settings!), ApiJson.Options));
		call.Output.Fields(lines);
		return ExitCodes.Success;
	}

	private static async Task<int> SetSettingsAsync(CommandCall call)
	{
		var category = call.Word("category");
		if (!SettingsCategories.Exists(category)) return call.NotFound("settings category");
		if (call.Options.Count == 0) throw CommandException.Invalid("Give at least one --field value.");

		var patch = new JsonObject();
		foreach (var (path, text) in call.Options)
		{
			var target = patch;
			var parts = path.Split('.');
			foreach (var part in parts[..^1])
				target = (target[part] ??= new JsonObject()) as JsonObject ??
				         throw CommandException.Invalid($"--{path} is not valid.");
			target[parts[^1]] = ValueOf(text);
		}

		var (read, current) = await SendAsync(call, actor => new GetSettings(actor));
		if (read != ServerOutcome.Ok) return call.Refuse(read);

		try
		{
			var changed = SettingsCategories.Patch(category, current!, patch);
			return call.Report(await SendAsync(call, actor => new UpdateSettings(actor, changed)), "Updated.");
		}
		catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidOperationException
			                           or FormatException)
		{
			throw CommandException.Invalid("The settings are not valid.");
		}
	}

	/// <summary>Reads a typed value: a number, true, false, null, a list or an object stays that; anything else is text.</summary>
	private static JsonNode? ValueOf(string text)
	{
		try
		{
			return JsonNode.Parse(text);
		}
		catch (JsonException)
		{
			return JsonValue.Create(text);
		}
	}

	private static async Task<int> AnnounceAsync(CommandCall call)
	{
		var text = call.Required("text");
		if (call.List("to") is not { Length: > 0 } ids)
		{
			var (all, sent) = await SendAsync(call, actor => new NotifyEveryone(actor, text));
			return all == ServerOutcome.Ok ? Announced(call, sent) : call.Refuse(all);
		}

		var recipients = new List<int>();
		foreach (var id in ids.Select(item =>
			         int.TryParse(item, out var parsed)
				         ? parsed
				         : throw CommandException.Invalid("--to must be user ids.")).Distinct())
		{
			if (await call.Get<ISender>().Send(new GetUser(UserReference.ById(id)), call.CancellationToken) is not { } user)
				throw CommandException.NotFound($"No such user: {id}.");
			recipients.Add(user.Id);
		}

		var (some, count) = await SendAsync(call, actor => new NotifyUsers(actor, text, recipients));
		return some == ServerOutcome.Ok ? Announced(call, count) : call.Refuse(some);
	}

	private static int Announced(CommandCall call, int sent)
	{
		call.Output.Ok($"Sent to {sent} clients.");
		return ExitCodes.Success;
	}

	private static async Task<int> ListScreenshotsAsync(CommandCall call)
	{
		var page = call.Page();
		var (result, list) = await SendAsync(call, actor => new ListScreenshots(actor));
		if (result != ServerOutcome.Ok) return call.Refuse(result);

		var views = list.Select(reference => new ScreenshotView(Path.GetFileName(reference.Value))).ToList();
		call.PrintPage(new Page<ScreenshotView>([.. views.Skip(page.Offset).Take(page.Limit)], page.Offset + page.Limit < views.Count), page);
		return ExitCodes.Success;
	}

	private static async Task<int> DeleteScreenshotAsync(CommandCall call)
	{
		var (listed, list) = await SendAsync(call, actor => new ListScreenshots(actor));
		if (listed != ServerOutcome.Ok) return call.Refuse(listed);

		var id = call.Word("id");
		var match = list.Where(reference => Path.GetFileName(reference.Value) == id).Cast<ContentReference?>()
			.FirstOrDefault();
		return match is null
			? call.NotFound("screenshot")
			: call.Report(await SendAsync(call, actor => new DeleteScreenshot(actor, match.Value)),
				"Screenshot deleted.");
	}
}