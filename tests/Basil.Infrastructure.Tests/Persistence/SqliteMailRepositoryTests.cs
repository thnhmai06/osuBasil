using Basil.Infrastructure.Persistence.Repositories;

namespace Basil.Infrastructure.Tests.Persistence;

/// <summary>Ported from app/repositories/mail.py, scoped to what login needs: unread mail delivery.</summary>
public class SqliteMailRepositoryTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>
{
    private readonly SqliteMailRepository _repository = new(fixture.ConnectionString);
    private readonly SqliteUserRepository _users = new(fixture.ConnectionString);

    [Fact]
    public async Task Create_ThenFetchUnread_ReturnsMailWithUsernames()
    {
        var recipient = await _users.CreateAsync("mail recipient", "mail-recipient@example.test", "hash", "xx");

        await _repository.CreateAsync(1, recipient.Id, "hello there");

        var unread = await _repository.FetchUnreadMailToUserAsync(recipient.Id);

        Assert.Single(unread);
        Assert.Equal("hello there", unread[0].Msg);
        Assert.Equal("BasilBot", unread[0].FromName);
        Assert.False(unread[0].Read);
    }

    [Fact]
    public async Task MarkConversationAsRead_ExcludesFromFutureUnreadFetch()
    {
        var recipient = await _users.CreateAsync("mail recipient 2", "mail-recipient2@example.test", "hash", "xx");
        await _repository.CreateAsync(1, recipient.Id, "msg one");

        await _repository.MarkConversationAsReadAsync(recipient.Id, 1);

        var unread = await _repository.FetchUnreadMailToUserAsync(recipient.Id);
        Assert.Empty(unread);
    }

    [Fact]
    public async Task FetchUnread_NoMail_ReturnsEmpty()
    {
        var recipient = await _users.CreateAsync("mail recipient 3", "mail-recipient3@example.test", "hash", "xx");

        Assert.Empty(await _repository.FetchUnreadMailToUserAsync(recipient.Id));
    }
}