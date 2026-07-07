using NSubstitute;
using OpenOsuTournament.Bancho.Application.Abstractions.Beatmaps;
using OpenOsuTournament.Bancho.Application.UseCases.Beatmaps;
using OpenOsuTournament.Bancho.Domain.Beatmaps;

namespace OpenOsuTournament.Bancho.Application.Tests.UseCases.Beatmaps;

/// <summary>Ported from app/services/direct_search.py's DirectSearchService, DB-backed instead of mirror-backed.</summary>
public class DirectSearchServiceTests
{
    private readonly IMapRepository _maps = Substitute.For<IMapRepository>();

    [Fact]
    public async Task NonTextQuery_PassesNullQueryThrough()
    {
        _maps.SearchAsync(null, null, null, 0, 100).Returns([]);

        await new DirectSearchService(_maps).SearchAsync(new DirectSearchRequest("Newest", -1, 4, 0));

        await _maps.Received(1).SearchAsync(null, null, null, 0, 100, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TextQuery_PassedThrough()
    {
        _maps.SearchAsync("camellia", null, null, 0, 100).Returns([]);

        await new DirectSearchService(_maps).SearchAsync(new DirectSearchRequest("camellia", -1, 4, 0));

        await _maps.Received(1).SearchAsync("camellia", null, null, 0, 100, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ModeNotMinusOne_FiltersByMode()
    {
        _maps.SearchAsync(null, GameMode.VanillaTaiko, null, 0, 100).Returns([]);

        await new DirectSearchService(_maps).SearchAsync(new DirectSearchRequest("Newest", 1, 4, 0));

        await _maps.Received(1).SearchAsync(null, GameMode.VanillaTaiko, null, 0, 100, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RankedStatusNotFour_ConvertsFromOsuDirect()
    {
        _maps.SearchAsync(null, null, RankedStatus.Ranked, 0, 100).Returns([]);

        // osudirect status 0 -> RankedStatus.Ranked (per RankedStatusExtensions.FromOsuDirect)
        await new DirectSearchService(_maps).SearchAsync(new DirectSearchRequest("Newest", -1, 0, 0));

        await _maps.Received(1).SearchAsync(null, null, RankedStatus.Ranked, 0, 100, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PageNum_MultipliedByOneHundredForOffset()
    {
        _maps.SearchAsync(null, null, null, 200, 100).Returns([]);

        await new DirectSearchService(_maps).SearchAsync(new DirectSearchRequest("Newest", -1, 4, 2));

        await _maps.Received(1).SearchAsync(null, null, null, 200, 100, Arg.Any<CancellationToken>());
    }
}