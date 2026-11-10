using Basil.Application.Common.Diagnostics;
using Basil.Infrastructure.Diagnostics.Services;
using Mediator;
using Microsoft.Extensions.Time.Testing;

namespace Basil.Infrastructure.Diagnostics.Tests.Services;

/// <summary>The sample merges the process measurements with what every module reports.</summary>
public sealed class DiagnosticsIntakeTests
{
	private static async Task<DiagnosticsSample> TakeAsync(params INotificationHandler<CollectDiagnostics>[] modules)
	{
		using var runtime = new RuntimeSampler(new FakeTimeProvider());
		return await new DiagnosticsIntake(runtime, modules).TakeSampleAsync(TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task TakeSample_MergesTheCountsOfEveryModuleIntoTheirCategories()
	{
		var sample = await TakeAsync(
			new Module(new DiagnosticsValue("application", "rooms", 2),
				new DiagnosticsValue("sessions", "stable", 0)),
			new Module(new DiagnosticsValue("application", "usersOnline", 5)));

		var application = (IReadOnlyDictionary<string, long>)sample["application"];
		Assert.Equal(2, application["rooms"]);
		Assert.Equal(5, application["usersOnline"]);
		Assert.Equal(0, ((IReadOnlyDictionary<string, long>)sample["sessions"])["stable"]);
	}

	[Fact]
	public async Task TakeSample_WhenACountIsReportedTwice_KeepsTheLastValue()
	{
		var sample = await TakeAsync(new Module(new DiagnosticsValue("application", "rooms", 1)),
			new Module(new DiagnosticsValue("application", "rooms", 4)));

		Assert.Equal(4, ((IReadOnlyDictionary<string, long>)sample["application"])["rooms"]);
	}

	[Fact]
	public async Task TakeSample_AlwaysHasTheProcessCategories_AndMarksTheUnmeasuredOnesUnavailable()
	{
		var sample = await TakeAsync();

		foreach (var category in new[] { "process", "gc", "threadpool", "runtime", "exceptions", "http" })
			Assert.True(sample.All.ContainsKey(category), category);
		foreach (var category in new[] { "database", "events" })
			Assert.Equal(false, sample[category].GetType().GetProperty("available")!.GetValue(sample[category]));
	}

	[Fact]
	public async Task TakeSample_ListsTheCategoriesInTheOrderOfTheApi()
	{
		var sample = await TakeAsync(new Module(new DiagnosticsValue("irc", "connections", 0)));

		var listed = DiagnosticsSample.Categories.Where(sample.All.ContainsKey).ToArray();
		Assert.Equal(listed, sample.All.Keys.ToArray());
	}

	private sealed class Module(params DiagnosticsValue[] values) : INotificationHandler<CollectDiagnostics>
	{
		public ValueTask Handle(CollectDiagnostics notification, CancellationToken ct)
		{
			foreach (var value in values) notification.Collector.Add(value);
			return ValueTask.CompletedTask;
		}
	}
}