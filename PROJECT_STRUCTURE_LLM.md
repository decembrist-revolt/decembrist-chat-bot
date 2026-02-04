# Decembrist Chat Bot - Project Structure

## Краткое описание

Telegram бот для группового чата канала, реализующий:
- **Антиспам**: Captcha, фильтрация сообщений, ограничение реакций
- **Инвентарная система**: Предметы для троллинга участников чата (проклятия, мины, чары и т.д.)
- **Социальные функции**: Лайки, дизлайки, лоры (записи), гивэвеи
- **Maze Game**: Многопользовательская игра-лабиринт с PvP элементами и призами

## Технологический стек

- **.NET 9.0** (C#)
- **MongoDB** — хранение данных
- **Telegram.Bot** — взаимодействие с Telegram API
- **Quartz.NET** — планировщик задач
- **Lamar** — DI-контейнер
- **LanguageExt** — функциональные расширения (Option, Either, etc.)
- **Serilog** — логирование
- **EmbedIO** — HTTP-сервер для health checks

---

## Структура директорий

```
DecembristChatBotSharp/
├── Program.cs              # Точка входа, запуск бота
├── AppConfig.cs            # Конфигурация приложения (record-based)
├── SetLogger.cs            # Настройка Serilog
├── UtilsExtensions.cs      # Общие extension-методы
├── appsettings.json        # Основные настройки
├── craftsettings.json      # Настройки крафта предметов
│
├── DI/                     # Dependency Injection
│   ├── DiContainer.cs      # Главный DI-контейнер (Lamar)
│   ├── HttpClientConfiguration.cs
│   ├── LamarJobFactory.cs  # Фабрика для Quartz jobs
│   ├── QuartzConfiguration.cs
│   └── TelegramConfiguration.cs
│
├── Entity/                 # Доменные модели (MongoDB документы)
│   ├── MemberItem.cs       # Предметы инвентаря (Box, Curse, Charm, Mina, etc.)
│   ├── NewMember.cs        # Новые участники (для captcha)
│   ├── FilterRecord.cs     # Записи антиспам фильтра
│   ├── RestrictMember.cs   # Ограниченные участники
│   ├── PremiumMember.cs    # Премиум-участники
│   ├── MinionInvitation.cs # Приглашения в миньоны (временные)
│   ├── MinionRelation.cs   # Активные связи миньон-мастер
│   ├── LoreUser.cs         # Пользователи с лорами
│   ├── MemberLike.cs       # Лайки участников
│   ├── DislikeMember.cs    # Дизлайки
│   ├── AdminUser.cs        # Администраторы
│   ├── WhiteListMember.cs  # Белый список
│   ├── UniqueItem.cs       # Уникальные предметы
│   ├── MazeGame.cs         # Игра-лабиринт и игроки
│   └── ...                 # Другие сущности
│
├── Mongo/                  # Репозитории MongoDB
│   ├── MongoDatabase.cs    # Подключение, сессии, индексы
│   ├── IRepository.cs      # Базовый интерфейс репозитория
│   ├── MemberItemRepository.cs  # Работа с инвентарём
│   ├── FilterRecordRepository.cs # Антиспам фильтры
│   ├── NewMemberRepository.cs    # Новые участники
│   ├── AdminRepository.cs        # Администраторы
│   ├── RestrictRepository.cs     # Ограничения
│   ├── MinionInvitationRepository.cs # Приглашения в миньоны
│   ├── MinionRepository.cs       # Связи миньон-мастер
│   ├── HistoryLogRepository.cs   # История действий
│   ├── MazeGameRepository.cs     # Игры-лабиринты и игроки
│   └── ...                       # Другие репозитории
│
├── Service/                # Бизнес-логика
│   ├── InventoryService.cs     # Инвентарь пользователя
│   ├── CraftService.cs         # Крафт предметов
│   ├── FilterService.cs        # Антиспам фильтрация
│   ├── BanService.cs           # Баны пользователей
│   ├── GiveService.cs          # Передача предметов
│   ├── OpenBoxService.cs       # Открытие ящиков
│   ├── DustService.cs          # Пыль (ресурс для крафта)
│   ├── AmuletService.cs        # Амулеты (защита)
│   ├── UniqueItemService.cs    # Уникальные предметы
│   ├── LoreService.cs          # Лоры (записи)
│   ├── ListService.cs          # Списки (админы, whitelist)
│   ├── CallbackService.cs      # Обработка callback-запросов
│   ├── PremiumMemberService.cs # Премиум-функции
│   ├── MinionService.cs        # Система миньонов (transfers, revocation)
│   ├── DeepSeekService.cs      # AI-интеграция
│   ├── RedditService.cs        # Reddit мемы
│   ├── TelegramPostService.cs  # Посты из Telegram
│   ├── MazeGameService.cs      # Логика игры лабиринт
│   ├── MazeGeneratorService.cs # Генерация лабиринтов
│   ├── MazeRendererService.cs  # Рендеринг изображений лабиринта
│   ├── MazeGameUiService.cs    # UI элементы для лабиринта
│   ├── MazeGameViewService.cs  # Отображение вида игрока
│   └── Buttons/                # Генерация inline-кнопок
│       ├── ProfileButton.cs
│       ├── ListButtons.cs
│       ├── LoreButtons.cs
│       └── AdminPanelButton.cs
│
├── Telegram/               # Telegram-обработчики
│   ├── BotHandler.cs       # Главный обработчик Update'ов
│   ├── LogAssistant.cs     # Логирование сообщений
│   ├── MessageAssistance.cs # Утилиты для сообщений
│   │
│   ├── MessageHandlers/    # Обработчики сообщений
│   │   ├── ChatMessageHandler.cs      # Сообщения в чатах
│   │   ├── PrivateMessageHandler.cs   # Личные сообщения
│   │   ├── MazeGameViewHandler.cs     # Запрос полной карты лабиринта
│   │   ├── NewChatMemberHandler.cs    # Новые участники
│   │   ├── CaptchaHandler.cs          # Captcha проверка
│   │   ├── FilteredMessageHandler.cs  # Фильтрация спама
│   │   ├── RestrictHandler.cs         # Ограничения участников
│   │   ├── CurseHandler.cs            # Проклятия (троллинг)
│   │   ├── CharmHandler.cs            # Чары (троллинг)
│   │   ├── MinaHandler.cs             # Мины (троллинг)
│   │   ├── MinionHandler.cs           # Обработка подтверждений миньонов
│   │   ├── FastReplyHandler.cs        # Быстрые ответы
│   │   ├── AccessLevelHandler.cs      # Проверка уровня доступа
│   │   ├── AiQueryHandler.cs          # AI запросы
│   │   └── ChatCommand/               # Команды чата
│   │       ├── ICommandHandler.cs     # Интерфейс команды
│   │       ├── InventoryCommandHandler.cs  # /inventory
│   │       ├── CraftCommandHandler.cs      # /craft
│   │       ├── LikeCommandHandler.cs       # /like
│   │       ├── DislikeCommandHandler.cs    # /dislike
│   │       ├── BanCommandHandler.cs        # /ban
│   │       ├── GiveItemCommandHandler.cs   # /give
│   │       ├── OpenBoxCommandHandler.cs    # /openbox
│   │       ├── SlotMachineCommandHandler.cs # /slot
│   │       ├── CurseCommandHandler.cs      # /curse
│   │       ├── CharmCommandHandler.cs      # /charm
│   │       ├── MinaCommandHandler.cs       # /mina
│   │       ├── MinionCommandHandler.cs     # /minion
│   │       ├── LoreCommandHandler.cs       # /lore
│   │       ├── PremiumCommandHandler.cs    # /premium
│   │       ├── ListCommandHandler.cs       # /list
│   │       ├── WhiteListCommandHandler.cs  # /whitelist
│   │       ├── RestrictCommandHandler.cs   # /restrict
│   │       ├── GiveawayCommandHandler.cs   # /giveaway
│   │       ├── RedditMemeCommandHandler.cs # /reddit
│   │       ├── TelegramMemeCommandHandler.cs # /meme
│   │       ├── DustCommandHandler.cs       # /dust
│   │       ├── FastReplyCommandHandler.cs  # /fastreply
│   │       ├── ShowLikesCommandHandler.cs  # /likes
│   │       ├── MazeGameCommandHandler.cs   # /mazegame
│   │       └── HelpChatCommandHandler.cs   # /help
│   │
│   ├── CallbackHandlers/   # Обработчики inline-кнопок
│   │   ├── ChatCallback/
│   │   │   ├── ChatCallbackHandler.cs
│   │   │   ├── GiveawayCallbackHandler.cs
│   │   │   ├── MazeGameJoinCallbackHandler.cs
│   │   │   └── ListCallbackHandler.cs
│   │   └── PrivateCallback/
│   │       ├── PrivateCallbackHandler.cs
│   │       ├── ProfilePrivateCallbackHandler.cs
│   │       ├── LorePrivateCallbackHandler.cs
│   │       ├── MazeGameCallbackHandlers.cs
│   │       └── FilterCallbackHandler.cs
│   │
│   └── LoreHandlers/       # Обработчики лоров
│       ├── LoreHandler.cs
│       ├── LoreKeyHandler.cs
│       ├── LoreContentHandler.cs
│       ├── LoreDeleteHandler.cs
│       └── LoreMessageAssistant.cs
│
├── Scheduler/              # Планировщик задач (Quartz)
│   ├── JobManager.cs       # Управление задачами
│   ├── IRegisterJob.cs     # Интерфейс регистрации
│   ├── CheckCaptchaJob.cs  # Проверка captcha (кик)
│   ├── CheckMinionConfirmationJob.cs # Проверка сообщений миньонов (раз в час)
│   ├── ExpiredMessageJob.cs # Удаление старых сообщений
│   ├── DailyDislikesResultsJob.cs  # Дневные результаты дизлайков
│   ├── DailyTopLikersGiftJob.cs    # Награда топ-лайкерам
│   ├── DailyPremiumRewardJob.cs    # Ежедневная награда премиум
│   ├── FastReplyExpiredJob.cs      # Истечение быстрых ответов
│   ├── PollPaymentJob.cs           # Оплата голосований
│   └── CheckBlackListCaptchaJob.cs # Проверка чёрного списка
│
├── Http/                   # HTTP сервер
│   └── HealthCheckServer.cs # Health check endpoint (для Docker)
│
├── Items/                  # Логика предметов
│   └── IPassiveItem.cs     # Интерфейс пассивных предметов
│
└── JsonConverter/          # Кастомные JSON-конвертеры
    ├── BsonDocumentJsonConverter.cs
    └── Iso8601TimeSpanConverter.cs
```

---

## Ключевые концепции

### Предметы (MemberItemType)

| Тип | Описание |
|-----|----------|
| `Box` | Ящик с рандомными предметами |
| `Curse` | Проклятие — спам реакциями на сообщения жертвы |
| `Charm` | Чары — положительные реакции на сообщения |
| `Mina` | Мина — сообщение удаляется при отправке |
| `Amulet` | Амулет — защита от проклятий |
| `RedditMeme` / `TelegramMeme` | Получение мема |
| `FastReply` | Быстрый ответ на сообщение |
| `SlotMachine` | Игра в слот-машину |
| `AiToken` | Токен для AI-запросов |
| `GreenDust` / `BlueDust` / `RedDust` / `Stone` | Ресурсы для крафта |

### Источники предметов (MemberItemSourceType)

- `Admin` — выдано администратором
- `Box` — из открытия ящика
- `TopLiker` — награда за топ лайков
- `PremiumDaily` — ежедневная премиум награда
- `Craft` — результат крафта
- `Give` — передача от другого игрока
- `SlotMachine` — выигрыш в слоте
- `Giveaway` — победа в гивэвее

---

## Конфигурация

Основные секции `appsettings.json`:

- `TelegramBotToken` — токен бота
- `MongoConfig` — подключение к MongoDB
- `CaptchaConfig` — настройки captcha
- `AllowedChatConfig` — разрешённые чаты
- `ItemConfig` — тексты инвентаря
- `CraftConfig` — рецепты крафта (в `craftsettings.json`)
- `SlotMachineConfig` — настройки слот-машины
- `CurseConfig`, `CharmConfig`, `MinaConfig` — настройки троллинга
- `PremiumConfig` — премиум функции
- `FilterConfig` — антиспам настройки
- `MazeConfig` — настройки игры лабиринт

---

## Поток обработки сообщений

```
Telegram Update
    ↓
BotHandler.HandleUpdateAsync()
    ↓
├─ Private Message → PrivateMessageHandler
├─ Chat Message → ChatMessageHandler
│   ├─ Command → ChatCommandHandler → конкретный ICommandHandler
│   ├─ Captcha → CaptchaHandler
│   ├─ Filter → FilteredMessageHandler
│   ├─ Curse/Charm/Mina → соответствующий Handler
│   └─ Regular → обработка лоров, реакций
├─ New Member → NewMemberHandler → Captcha flow
└─ Callback → ChatCallbackHandler / PrivateCallbackHandler
```

---

## База данных (MongoDB Collections)

- `member_items` — инвентарь участников
- `new_members` — новые участники (captcha)
- `filter_records` — антиспам фильтры
- `restrict_members` — ограниченные участники
- `premium_members` — премиум участники
- `minion_invitations` — приглашения в миньоны (временные)
- `minion_relations` — активные связи миньон-мастер
- `lore_users` — пользователи с лорами
- `member_likes` — лайки
- `dislike_members` — дизлайки
- `admin_users` — администраторы
- `whitelist_members` — белый список
- `history_logs` — история действий
- `unique_items` — уникальные предметы
- `expired_messages` — сообщения для автоудаления
- `fast_replies` — быстрые ответы
- `giveaway_participants` — участники гивэвеев
- `maze_games` — активные игры лабиринт
- `maze_players` — игроки в лабиринтах

---

## Docker

- `Dockerfile` — сборка .NET приложения
  - **Важно**: Включает установку нативных зависимостей для SkiaSharp (`libfontconfig1`, `libgomp1`)
  - Необходимо для работы MazeGameService и генерации изображений лабиринта
- `docker-compose.yml` — запуск с MongoDB
- `data/` — volume для MongoDB данных

