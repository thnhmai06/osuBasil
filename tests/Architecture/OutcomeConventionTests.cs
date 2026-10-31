using System.Globalization;

namespace Basil.ArchitectureTests;

/// <summary>
///     Pins the convention that makes <c>Result&lt;TOutcome&gt;</c> safe: a denied result carries the default outcome, so
///     success must never be the default value of an outcome enum.
/// </summary>
public class OutcomeConventionTests
{
	private static IEnumerable<Type> OutcomeEnums()
	{
		return ModuleAssemblies.Applications.SelectMany(assembly => assembly.GetTypes()).Where(type =>
			type is { IsEnum: true, IsPublic: true } && type.Name.EndsWith("Outcome", StringComparison.Ordinal));
	}

	[Fact]
	public void OutcomeEnums_AreFoundInTheModules_PositiveControl()
	{
		Assert.True(OutcomeEnums().Count() >= 6);
	}

	[Fact]
	public void OutcomeEnums_HaveOkEqualToOne_SoTheDefaultValueIsNeverSuccess()
	{
		foreach (var outcome in OutcomeEnums())
		{
			Assert.True(Enum.IsDefined(outcome, "Ok"), $"{outcome.Name} has no Ok member.");
			Assert.Equal(1, Convert.ToInt32(Enum.Parse(outcome, "Ok"), CultureInfo.InvariantCulture));
			Assert.False(
				Enum.GetNames(outcome).Any(name =>
					Convert.ToInt32(Enum.Parse(outcome, name), CultureInfo.InvariantCulture) == 0),
				$"{outcome.Name} defines a member for the value 0, which means \"not set\".");
		}
	}
}