using System.Net;
using Basil.Domain.Authentication;
using Basil.Domain.Client;
using Basil.Domain.Common;
using Basil.Domain.Users;

namespace Basil.Domain.Tests.Authentication;

public class SessionRecordTests
{
	private static readonly DateTimeOffset Start = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

	[Fact]
	public void Equals_SameId_IsTrueWhateverTheDataIs()
	{
		var id = Guid.NewGuid();
		var first = Make(id: id);
		var later = Make(id: id, lastActiveAt: Start.AddHours(1), tokenHash: [1, 2, 3]);

		Assert.Equal(first, later);
		Assert.Equal(first.GetHashCode(), later.GetHashCode());
	}

	[Fact]
	public void Equals_DifferentId_IsFalseWhateverTheDataIs()
	{
		Assert.NotEqual(Make(id: Guid.NewGuid()), Make(id: Guid.NewGuid()));
	}

	[Fact]
	public void New_EmptyId_Throws()
	{
		Assert.Throws<ArgumentException>(() => Make(id: Guid.Empty));
	}

	[Fact]
	public void New_UndefinedType_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Make(type: 0));
	}

	[Theory]
	[InlineData(SessionType.Stable)]
	[InlineData(SessionType.StableTourney)]
	public void New_OsuKindWithoutClient_Throws(SessionType type)
	{
		Assert.Throws<ArgumentException>(() => Make(type: type, client: null));
	}

	[Theory]
	[InlineData(SessionType.Stable)]
	[InlineData(SessionType.StableTourney)]
	public void New_OsuKindWithClient_IsAccepted(SessionType type)
	{
		Assert.Equal(type, Make(type: type, client: Client()).Type);
	}

	[Theory]
	[InlineData(SessionType.Irc)]
	[InlineData(SessionType.Api)]
	[InlineData(SessionType.Bot)]
	public void New_NonOsuKindWithoutClient_IsAccepted(SessionType type)
	{
		Assert.Equal(type, Make(type: type, client: null).Type);
	}

	[Fact]
	public void New_ActiveBeforeItStarted_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Make(lastActiveAt: Start.AddTicks(-1)));
	}

	[Fact]
	public void New_ActiveExactlyWhenItStarted_IsAccepted()
	{
		Assert.Equal(Start, Make(lastActiveAt: Start).Value.LastActiveAt);
	}

	private static SessionRecord Make(Guid? id = null, SessionType type = SessionType.Api, OsuClient? client = null,
		DateTimeOffset? lastActiveAt = null, byte[]? tokenHash = null)
	{
		var user = new UserRecord { Id = 1, Value = new UserData { Name = "session_user" } };
		return new SessionRecord(id ?? Guid.NewGuid(), user, type, Start,
			new SessionData(new SessionDevice(IPAddress.Loopback, client), new SessionCredential(tokenHash),
				lastActiveAt ?? Start));
	}

	private static OsuClient Client()
	{
		return new OsuClient(new OsuVersion(new DateOnly(2026, 1, 2), null, OsuVersionStream.Stable),
			new OsuFingerprint(new Md5("0cc175b9c0f1b6a831c399e269772661"),
				new Md5("4a8a08f09d37b73795649038408b5f33"), new Md5("f97c5d29941bfb1b2fdab0874906ab82"),
				new NetworkAdapters
				{
					Adapters = "adapter-one.", Hash = new Md5("92eb5ffee6ae2fec3ad71c777531578f")
				}));
	}
}