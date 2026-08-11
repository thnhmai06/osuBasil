<div align="center">

<img src="assets/icon.png" width="160" alt="Basil">

# Basil

<sub><i>If [Akatsuki](https://github.com/osuAkatsuki/bancho.py) means dawn, then Basil is the sunflower that always
faces the sun.</i></sub>

**A lightweight, high-performance [osu!](https://osu.ppy.sh/) (stable) server for tournaments and multiplayer.**

[![CI](https://img.shields.io/github/actions/workflow/status/thnhmai06/osuBasil/ci.yml?branch=main&label=CI&style=flat-square)](https://github.com/thnhmai06/osuBasil/actions)
[![CodeFactor](https://www.codefactor.io/repository/github/thnhmai06/osubasil/badge/main?style=flat-square)](https://www.codefactor.io/repository/github/thnhmai06/osubasil/overview/main)
[![License](https://img.shields.io/github/license/thnhmai06/osuBasil?style=flat-square)](LICENSE.md)
[![Last commit](https://img.shields.io/github/last-commit/thnhmai06/osuBasil?style=flat-square)](https://github.com/thnhmai06/osuBasil/commits/main)
[![GitHub Release](https://img.shields.io/github/v/release/thnhmai06/osuBasil?sort=semver&logo=github&logoColor=white&style=flat-square)](https://github.com/thnhmai06/osuBasil/releases/latest)
[![Docker Image Version](https://img.shields.io/docker/v/thnhmai06/osubasil?sort=semver&logo=docker&logoColor=white&style=flat-square)](https://hub.docker.com/repository/docker/thnhmai06/osubasil/)

</div>

> [!IMPORTANT]
> **Disclaimer.**
> This project is not affiliated with, endorsed by, or connected to [osu!](https://osu.ppy.sh/)
([ppy Pty Ltd](https://ppy.sh/)) or [bancho.py](https://github.com/osuAkatsuki/bancho.py)
([Akatsuki](https://github.com/osuAkatsuki)). The project name **"Basil"** was inspired by the character Basil
from [OMORI](https://www.omori-game.com/), and the mascot artwork depicts the same character. This project is not
affiliated with, endorsed by, or connected to OMORI or its developer, [OMOCAT](https://omocat.com/). All character
rights belong to their respective owners.

## ✨ Key features

Basil is designed around a simple principle: **keep the server self-contained and minimize external dependencies**.

The server can operate entirely offline, keeping gameplay and tournament data under the operator's control without
requiring third-party online services.

* **Multiplayer-first**: provides the [osu! stable multiplayer](https://osu.ppy.sh/wiki/en/Client/Interface/Multiplayer)
  experience required for tournament operation, while deliberately excluding unrelated singleplayer and social features.
* **Tournament support**: supports [osu!tourney](https://osu.ppy.sh/wiki/en/osu%21_tournament_client/osu%21tourney),
  tournament-oriented `!mp` commands, live match state, and real-time reporting.
* **osu! ecosystem compatibility**:
  supports [osu!direct](https://osu.ppy.sh/community/forums/topics/1433039), [BanchoBot](https://osu.ppy.sh/wiki/en/BanchoBot)
  (as BasilBot), and [IRC](https://osu.ppy.sh/wiki/en/Community/Internet_Relay_Chat).
* **Self-contained storage**: stores server data locally using SQLite; no external database server is required.
* **Offline operation**: core gameplay does not depend on the [osu!api](https://osu.ppy.sh/wiki/en/osu%21api) or
  external beatmap mirrors.
* **Tournament HTTP API**: provides HTTP and SSE endpoints for tournament management, match reports, spectating, and
  real-time multiplayer data, with an [OpenAPI](https://spec.openapis.org/oas/latest.html) specification and interactive
  documentation through [Scalar](https://scalar.com/).
* **Stable-focused**: targets the osu! stable client and its multiplayer/tournament workflows rather than attempting to
  reproduce the complete osu! server.

For the complete supported and excluded feature set, see
[`docs/for-developers/working-scopes.md`](docs/for-developers/working-scopes.md).

## 🛠️ Tech stack

| Layer          | Choice                                                                                                                                                                   |
|----------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Runtime        | [.NET](https://dot.net/) 10 with [ASP.NET Core](https://asp.net/), distributed as self-contained executables and supported in [Docker](https://docker.com)               |
| Database       | [SQLite](https://www.sqlite.org/), accessed through [Dapper](https://github.com/DapperLib/Dapper) and versioned with [DbUp](https://dbup.readthedocs.io/)                |
| API            | Build-time [OpenAPI](https://spec.openapis.org/oas/latest.html) generation with interactive documentation powered by [Scalar](https://scalar.com/)                       |
| Star rating    | Official osu! difficulty and performance calculation algorithms from [osu!lazer](https://github.com/ppy/osu)                                                             |
| Beatmap assets | Thumbnails processed with [ImageSharp](https://sixlabors.com/products/imagesharp/); audio previews generated with [FFMpegCore](https://github.com/rosenbjerg/FFMpegCore) |
| Security       | Passwords hashed with [BCrypt.Net](https://github.com/BcryptNet/bcrypt.net)                                                                                              |
| Logging        | Structured logging with [Serilog](https://serilog.net/)                                                                                                                  |
| Testing        | [xUnit](https://xunit.net/), [NSubstitute](https://nsubstitute.github.io/), and [NetArchTest](https://github.com/BenMorris/NetArchTest)                                  |

## 📖 Getting Started

Basil's documentation is organized by audience.

### Client

For connecting an osu! client or using Basil's HTTP/SSE interfaces:

* Browse the interactive API and BasilBot documentation at `api.<domain>/docs/` on a running instance.
* Browse the published API documentation on [GitHub Pages](https://thnhmai06.github.io/osuBasil/).
* See [`docs/for-client/bancho/getting-started.md`](docs/for-client/bancho/getting-started.md) for connecting an osu!
  client.

### Server operator

To deploy and operate a Basil server:

* See [`docs/for-technicians/deployment.md`](docs/for-technicians/deployment.md) for deployment
* See [`docs/for-technicians/docker.md`](docs/for-technicians/docker.md) for Docker deployment

### Developer

To work on Basil itself:

* See [`docs/for-developers/architecture.md`](docs/for-developers/architecture.md) for system architecture
* See [`docs/for-developers/working-scopes.md`](docs/for-developers/working-scopes.md) for supported and excluded
  functionality
* See [`docs/for-developers/testing.md`](docs/for-developers/testing.md) for testing policy

### Documentation map

See [`docs/index.md`](docs/index.md) for the complete documentation structure.

### Contributing

See [`CONTRIBUTING.md`](CONTRIBUTING.md) for contribution guidelines.

## ❤️ Credits

**Basil** is built on top of [**bancho.py**](https://github.com/osuAkatsuki/bancho.py)
by [Akatsuki](https://github.com/osuAkatsuki).

Many thanks to the Akatsuki team for creating and maintaining such an amazing project, which laid the foundation and
inspiration for Basil.

### 💻 Contributors

| [<img src="https://github.com/thnhmai06.png" width="100"><br><sub>**thnhmai06**</sub>](https://github.com/thnhmai06) |
|:--------------------------------------------------------------------------------------------------------------------:|
|                      <span title="Project Manager">👑</span> <span title="Developer">💻</span>                       |  

## ⭐ Star History

[![Star History Chart](https://api.star-history.com/chart?repos=thnhmai06/osuBasil&type=date&legend=top-left&sealed_token=wPQ_eLQYxDpC8IxGbg3aO7Pj4XQ1Pxr5Y16JLxzXZkGFuytVDcgJBdCUlsx9wbZzySsHPkAAj3L9OO5nOCpSebEGkL8fFpPoUwZSSgEHqj1RSWZgLn_G2Vuqc0itECn1WFYXPG74tJN9U1OzQoMcvyLnW8NBycp-yaxWQmDu-rlmTRVhvMpW3LGys9r1)](https://www.star-history.com/?repos=thnhmai06%2FosuBasil&type=date&legend=top-left)
