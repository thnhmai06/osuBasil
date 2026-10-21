using Basil.Application.Runtime.Sessions;
using Basil.Application.Runtime.Tests.Support;

namespace Basil.Application.Runtime.Tests.Common;

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

	private sealed class FlagsScope : Scoped<object, SampleBits>
	{
		public FlagsScope(Connection by, TimeProvider clock, SampleBits held) : base(new object(), by, clock)
		{
			Rights = held;
		}

		public SampleBits? Need(SampleBits required)
		{
			return RequireRight(required);
		}
	}

	private sealed class LevelScope : Scoped<object, SampleLevel>
	{
		public LevelScope(Connection by, TimeProvider clock, SampleLevel held) : base(new object(), by, clock)
		{
			Rights = held;
		}

		public SampleLevel? Need(SampleLevel required)
		{
			return RequireRight(required);
		}
	}

	[Fact]
	public async Task RequireRight_FlagsHoldingEveryRequiredBit_IsMet()
	{
		using var app = new TestApplication();
		var by = await app.LoginAsync(await app.CreateUserAsync("FlagsHolder", "password"));
		var scope = new FlagsScope(by, app.Clock, SampleBits.Read | SampleBits.Write);

		Assert.Null(scope.Need(SampleBits.Read));
		Assert.Null(scope.Need(SampleBits.Read | SampleBits.Write));
	}

	[Fact]
	public async Task RequireRight_FlagsMissingABit_NamesOnlyTheMissingBits()
	{
		using var app = new TestApplication();
		var by = await app.LoginAsync(await app.CreateUserAsync("FlagsMissing", "password"));
		var scope = new FlagsScope(by, app.Clock, SampleBits.Read);

		Assert.Equal(SampleBits.Write | SampleBits.Delete, scope.Need(SampleBits.Read | SampleBits.Write | SampleBits.Delete));
	}

	[Fact]
	public async Task RequireRight_FlagsRequiringNothing_IsMet()
	{
		using var app = new TestApplication();
		var by = await app.LoginAsync(await app.CreateUserAsync("FlagsNothing", "password"));
		var scope = new FlagsScope(by, app.Clock, SampleBits.None);

		Assert.Null(scope.Need(SampleBits.None));
	}

	[Theory]
	[InlineData(SampleLevel.Admin, SampleLevel.Member, true)]
	[InlineData(SampleLevel.Member, SampleLevel.Member, true)]
	[InlineData(SampleLevel.Guest, SampleLevel.Guest, true)]
	[InlineData(SampleLevel.Member, SampleLevel.Admin, false)]
	[InlineData(SampleLevel.Guest, SampleLevel.Member, false)]
	public async Task RequireRight_Levels_AreMetWhenHeldIsAtLeastRequired(SampleLevel held, SampleLevel required, bool met)
	{
		using var app = new TestApplication();
		var by = await app.LoginAsync(await app.CreateUserAsync("LevelHolder", "password"));
		var scope = new LevelScope(by, app.Clock, held);

		var missing = scope.Need(required);

		Assert.Equal(met ? null : required, missing);
	}
}
