using Basil.Application.Common;
using Basil.Application.Users.Objects.Connections;
using Basil.Testing;

namespace Basil.Host.Tests.Common;

public class ScopedRightsTests
{
	[Flags]
	public enum SampleBits
	{
		None = 0,
		Read = 1,
		Write = 2,
		Delete = 4
	}

	public enum SampleLevel
	{
		Guest = 0,
		Member = 1,
		Admin = 2
	}

	[Fact]
	public async Task RequireRight_FlagsHoldingEveryRequiredBit_IsMet()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("FlagsHolder", "password"));
		var scope = new FlagsScope(actor, SampleBits.Read | SampleBits.Write);

		Assert.Null(scope.Need(SampleBits.Read));
		Assert.Null(scope.Need(SampleBits.Read | SampleBits.Write));
	}

	[Fact]
	public async Task RequireRight_FlagsMissingABit_NamesOnlyTheMissingBits()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("FlagsMissing", "password"));
		var scope = new FlagsScope(actor, SampleBits.Read);

		Assert.Equal(SampleBits.Write | SampleBits.Delete,
			scope.Need(SampleBits.Read | SampleBits.Write | SampleBits.Delete));
	}

	[Fact]
	public async Task RequireRight_FlagsRequiringNothing_IsMet()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("FlagsNothing", "password"));
		var scope = new FlagsScope(actor, SampleBits.None);

		Assert.Null(scope.Need(SampleBits.None));
	}

	[Theory]
	[InlineData(SampleLevel.Admin, SampleLevel.Member, true)]
	[InlineData(SampleLevel.Member, SampleLevel.Member, true)]
	[InlineData(SampleLevel.Guest, SampleLevel.Guest, true)]
	[InlineData(SampleLevel.Member, SampleLevel.Admin, false)]
	[InlineData(SampleLevel.Guest, SampleLevel.Member, false)]
	public async Task RequireRight_Levels_AreMetWhenHeldIsAtLeastRequired(SampleLevel held, SampleLevel required,
		bool met)
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("LevelHolder", "password"));
		var scope = new LevelScope(actor, held);

		var missing = scope.Need(required);

		Assert.Equal(met ? null : required, missing);
	}

	private sealed class FlagsScope(Connection actor, SampleBits held) : Scoped<SampleBits>(actor, held)
	{
		public SampleBits? Need(SampleBits required)
		{
			return Require(required);
		}
	}

	private sealed class LevelScope(Connection actor, SampleLevel held) : Scoped<SampleLevel>(actor, held)
	{
		public SampleLevel? Need(SampleLevel required)
		{
			return Require(required);
		}
	}
}