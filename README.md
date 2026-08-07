<div align="center">

<img src="assets/icon.png" width="160" alt="Basil">

# Basil

<sub><i>If [Akatsuki](https://github.com/osuAkatsuki) means dawn, then Basil is the sunflower that always facing to the
sun.</i></sub>

**A lightweight, high-performance [osu!](https://osu.ppy.sh/) (stable) server for tournaments and multiplayer.**

[![CI](https://img.shields.io/github/actions/workflow/status/thnhmai06/osuBasil/ci.yml?branch=main&label=CI&style=flat-square)](https://github.com/thnhmai06/osuBasil/actions)
[![License](https://img.shields.io/github/license/thnhmai06/osuBasil?style=flat-square)](LICENSE.md)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white&style=flat-square)](https://dotnet.microsoft.com/)
[![Last commit](https://img.shields.io/github/last-commit/thnhmai06/osuBasil?style=flat-square)](https://github.com/thnhmai06/osuBasil/commits/main)

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

At Basil, our philosophy is simple: **minimize external dependencies**. Basil is built to operate **completely
offline**, giving you full control without requiring internet connectivity or third-party online services:

- **Replicates the full multiplayer experience of [osu!Bancho](https://osu.ppy.sh/wiki/en/Bancho_%28server%29)**, while
  intentionally omitting singleplayer ranking and other unrelated features.
- **Supports [osu!direct](https://osu.ppy.sh/community/forums/topics/1433039),
  [osu!tourney](https://osu.ppy.sh/wiki/en/osu%21_tournament_client/osu%21tourney),
  [BanchoBot](https://osu.ppy.sh/wiki/en/BanchoBot) (as BasilBot),
  and [IRC](https://osu.ppy.sh/wiki/en/Community/Internet_Relay_Chat)**.
- **Stores all data locally**, requiring no external services or database server.
- **Runs entirely offline**, with no dependency on the [osu!api](https://osu.ppy.sh/wiki/en/osu%21api) or beatmap
  mirrors for core gameplay.
- **Provides a comprehensive HTTP API** for tournament management, spectating, and real-time multiplayer data,
  documented with [OpenAPI](https://www.openapis.org/) and browsable through [Scalar](https://scalar.com/).

## 🛠️ Tech stack

| Layer          | Choice                                                                                                                                                                                         |
|----------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Runtime        | [.NET](https://dot.net/) 10 with [ASP.NET Core](https://asp.net/), distributed as a standalone executable. Support [Docker](https://www.docker.com/).                                          |
| Database       | [SQLite](https://www.sqlite.org/), accessed via [Dapper](https://github.com/DapperLib/Dapper) and versioned with [DbUp](https://dbup.readthedocs.io/).                                         |
| API            | Build-time [OpenAPI](https://www.openapis.org/) generation with interactive documentation powered by [Scalar](https://scalar.com/).                                                            |
| Star rating    | Uses the official difficulty and performance calculation algorithms from [osu!lazer](https://github.com/ppy/osu).                                                                              |
| Beatmap assets | Thumbnails processed with [ImageSharp](https://sixlabors.com/products/imagesharp/); audio previews generated with [FFMpegCore](https://github.com/rosenbjerg/FFMpegCore).                      |
| Security       | Passwords hashed using [BCrypt.Net](https://github.com/BcryptNet/bcrypt.net).                                                                                                                  |
| Logging        | Structured logging with [Serilog](https://serilog.net/).                                                                                                                                       |
| Testing        | [xUnit](https://xunit.net/) with [NSubstitute](https://nsubstitute.github.io/) for unit tests and [NetArchTest](https://github.com/BenMorris/NetArchTest) to enforce architectural boundaries. |

## 📖 Getting Started

- **API, Bot Commands, Client** – Browse the interactive documentation at `api.<domain>/docs/` on any Basil instance, or view
  the same docs on [GitHub Pages](https://thnhmai06.github.io/osuBasil/) without running a server.
- **Run a Server** – See [`docs/run-deployment.md`](docs/run-deployment.md) for deployment, local development, and
  connecting an osu! client.
- **Architecture** – See [`docs/architecture.md`](docs/architecture.md) for the system architecture and links to every
  subsystem.
- **Project Scope** – See [`docs/working-scopes.md`](docs/working-scopes.md) for the project's goals, supported
  features, and intentional limitations.

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
