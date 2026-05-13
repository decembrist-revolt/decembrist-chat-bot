# AGENTS.md

## Project Overview

Decembrist Chat Bot is a .NET 9 C# Telegram bot for group chats. It handles moderation, captcha/anti-spam flows, inventory items, premium rewards, likes/dislikes, lore records, giveaways, minion relations, AI queries, Reddit/Telegram memes, and a maze game.

The main project is `DecembristChatBotSharp/`; the solution file is `DecembristChatBot.sln`.

## Tech Stack

- .NET 9.0, C# with nullable reference types enabled.
- `Telegram.Bot` for Telegram updates and API calls.
- MongoDB for persistence, including Quartz job store data.
- Quartz.NET for scheduled jobs.
- Lamar for dependency injection and assembly scanning.
- LanguageExt for `Option`, `TryAsync`, `Unit`, and related functional helpers.
- Serilog for logging.
- EmbedIO for the health check server.
- SkiaSharp for image rendering, notably maze rendering.

## Repository Layout

- `DecembristChatBotSharp/Program.cs` starts logging, DI, health checks, Mongo checks/indexes, Telegram receiving, and Quartz jobs.
- `DecembristChatBotSharp/AppConfig.cs` defines strongly typed configuration records and recursive validation.
- `DecembristChatBotSharp/DI/` contains Lamar registrations for Telegram, HTTP clients, Quartz, and jobs.
- `DecembristChatBotSharp/Entity/` contains Mongo document/domain models.
- `DecembristChatBotSharp/Mongo/` contains repositories and `MongoDatabase`.
- `DecembristChatBotSharp/Service/` contains business logic and inline button builders.
- `DecembristChatBotSharp/Telegram/` contains update routing, message handlers, command handlers, callback handlers, and lore handlers.
- `DecembristChatBotSharp/Scheduler/` contains Quartz jobs and job registration.
- `DecembristChatBotSharp/Http/` contains health checks.
- `DecembristChatBotSharp/Items/` contains passive item logic.
- `PROJECT_STRUCTURE_LLM.md` has a fuller architectural summary in Russian and is useful orientation material.

## Build And Run

Use these commands from the repository root:

```powershell
dotnet restore .\DecembristChatBot.sln
dotnet build .\DecembristChatBot.sln
```

For local MongoDB:

```powershell
docker compose up -d mongodb
```

Run the bot only when real configuration is available:

```powershell
dotnet run --project .\DecembristChatBotSharp\DecembristChatBotSharp.csproj
```

Configuration is loaded from `appsettings.json`, `craftsettings.json`, `chatConfigTemplate.json`, environment variables, and an in-memory `DeployTime`. Environment variables can override nested config values with the standard double-underscore form, for example `MongoConfig__ConnectionString` or `DeepSeekConfig__Enabled`.

There is no test project in the current tree. When changing behavior, at minimum run `dotnet build .\DecembristChatBot.sln`; add focused tests if a test project is introduced later or if the change justifies creating one.

## Dependency Injection Patterns

Lamar scans the calling assembly with default conventions and registers all implementations of these interfaces:

- `ICommandHandler`
- `IPrivateCallbackHandler`
- `IChatCallbackHandler`
- `IPassiveItem`
- `IRepository`
- `IRegisterJob`

Prefer constructor injection. Most services and handlers are discovered by naming/default convention or by implemented interfaces; only add explicit registrations in `DI/` when scanning is not enough.

Some classes use Lamar attributes such as `[Singleton]`. Preserve the existing lifetime model when modifying existing services.

## Telegram Flow

`BotHandler` is the central update router. It accepts messages, edited messages, chat member updates, and callback queries. It drops stale message/member updates using `AppConfig.UpdateExpirationSeconds`.

Common handler areas:

- Group messages go through `ChatMessageHandler`.
- Private messages go through `PrivateMessageHandler`.
- Chat commands live under `Telegram/MessageHandlers/ChatCommand/` and implement `ICommandHandler`.
- Chat callbacks live under `Telegram/CallbackHandlers/ChatCallback/` and implement `IChatCallbackHandler`.
- Private callbacks live under `Telegram/CallbackHandlers/PrivateCallback/` and implement `IPrivateCallbackHandler`.
- Callback data should be created and parsed through `CallbackService` helpers rather than ad hoc string parsing.
- Inline bot moderation is handled during group message processing: `BotHandler` copies `Message.ViaBot.Username` into `ChatMessageHandlerParams`, and `InlineBotRestrictionHandler` deletes messages from restricted inline bots unless the sender is an admin for that chat.

When adding a command, implement `ICommandHandler`, set `Command`, `Description`, and `CommandLevel`, and keep access/rate-limit behavior consistent with nearby command handlers.

The `/restrictInlineBot [bot-username]` command is admin-only and lives under `Telegram/MessageHandlers/ChatCommand/`. It stores usernames normalized without `@` and in lowercase. Use `unblock` or the shared `clear` subcommand to remove an inline bot restriction for the current chat.

## MongoDB Patterns

Use `MongoDatabase.GetCollection<T>(collectionName)` for collections and `MongoDatabase.OpenSession()` for transactions/session-aware operations.

Repositories implement `IRepository`; override `EnsureIndexes()` when a collection needs indexes. `Program.cs` calls `MongoDatabase.EnsureIndexes()` during startup.

Prefer typed MongoDB filter/update builders over stringly typed queries. Keep collection names and index definitions close to the repository that owns the data.

Chat-specific inline bot restrictions are stored in the `RestrictedInlineBot` collection through `RestrictedInlineBotRepository`, keyed by `(ChatId, Username)`. Keep this storage separate from user restrictions and whitelists.

## Scheduler Patterns

Quartz jobs live in `Scheduler/` and implement `IRegisterJob`. Each job owns its `TriggerKey` and `Register(IScheduler scheduler)` method.

Cron strings in config are generally UTC where the property name says `CronUtc`. Avoid silently changing schedules or time zones.

Quartz uses MongoDB as its job store through `Quartz.Spi.MongoDbJobStore`, so local runs need a reachable MongoDB replica set.

## Configuration And Secrets

Do not commit real Telegram tokens, Reddit credentials, DeepSeek bearer tokens, Keycloak secrets, production MongoDB connection strings, chat IDs, or admin IDs.

`appsettings.json` currently contains placeholder and local values. Treat it as a development template unless the user explicitly says otherwise. Prefer environment variable overrides for secrets.

Be careful when editing localized message text in JSON. Some existing Russian strings appear mojibake-encoded in the checked-in file; do not perform broad encoding rewrites unless the task is specifically about fixing encoding.

## Code Style

- Follow the existing file-scoped namespace style.
- Keep nullable annotations meaningful; avoid suppressions unless there is a clear reason.
- Use async APIs all the way through for Telegram, Mongo, HTTP, and Quartz work.
- Preserve LanguageExt idioms already present in the area you edit (`Option`, `Match`, `ToTryAsync`, `Unit`).
- Use concise Serilog structured logging for operationally useful failures.
- Prefer small focused services/handlers over adding large branches to central routers.
- Keep comments sparse and useful.

## Operational Cautions

- Bot startup contacts Telegram with `GetMe`; do not run the bot unless the token and network behavior are intentional.
- Mongo connection checks and Quartz startup require MongoDB to be available.
- Logs under `DecembristChatBotSharp/logs/` are generated artifacts; avoid editing or committing new logs.
- `data/`, `.idea/`, `.sln.DotSettings.user`, and local runtime files should usually be left alone unless explicitly requested.
- Do not change command names, callback prefixes, collection names, or persisted enum values casually; they may be part of user-facing behavior or stored data.
- Inline bot restriction usernames are persisted in normalized form; preserve that normalization when changing command parsing or message checks.
- When changing inventory, premium, minion, maze, or moderation logic, inspect related services, handlers, repositories, scheduled jobs, and button/callback code together. These features often cross module boundaries.

## Git Notes For Agents

The workspace may be dirty. Never revert user changes unless explicitly asked.

In this environment, Git may report `detected dubious ownership` for the repository. Do not modify global Git config just to inspect files; ask before changing Git trust settings if Git operations are required.
