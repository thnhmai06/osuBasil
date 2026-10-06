using Basil.Application.Storage.Contracts.Chat;
using Basil.Domain.Chat;
using Basil.Domain.Users;
using Dapper;

namespace Basil.Infrastructure.Storage.Chat;

/// <summary>Stores the general chat channels the server offers.</summary>
internal sealed class SqliteChannelRepository(Database database) : IChannelRepository
{
	/// <inheritdoc />
	public async Task<IReadOnlyList<GeneralChannel>> ListAsync(CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		var rows = await connection.QueryAsync<ChannelRow>(
			"SELECT Name, Topic, ReadPermissions, WritePermissions, AutoJoin, Visible FROM Channels ORDER BY Name");

		return [.. rows.Select(row => row.ToChannel())];
	}

	/// <summary>A stored row of the Channels table.</summary>
	private sealed class ChannelRow
	{
		public string Name { get; set; } = "";
		public string Topic { get; set; } = "";
		public long ReadPermissions { get; set; }
		public long WritePermissions { get; set; }
		public bool AutoJoin { get; set; }
		public bool Visible { get; set; }

		/// <summary>Builds a general channel from this row.</summary>
		public GeneralChannel ToChannel()
		{
			return new GeneralChannel
			{
				Name = Name,
				Topic = Topic,
				ReadPermissions = (Permissions)(ulong)ReadPermissions,
				WritePermissions = (Permissions)(ulong)WritePermissions,
				AutoJoin = AutoJoin,
				Visible = Visible
			};
		}
	}
}
