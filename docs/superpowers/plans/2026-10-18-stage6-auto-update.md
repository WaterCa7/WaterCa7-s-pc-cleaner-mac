# Этап 6 «Автообновление» — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** программа сама находит новую версию в релизах Mac-репозитория, проверяет подпись `update.json`, скачивает
zip, при следующем запуске (или по кнопке) ставит новую `.app` на место текущей и перезапускается; при любой ошибке
прежняя программа остаётся. Настройки: «Обновляться автоматически», «Проверить сейчас», состояние; полоса над страницей.

**Architecture:** перенос схемы Windows 1.1.0 (`src/PcCleaner/Core/Updates/*` Windows-репозитория): `UpdateManifest`
(платформа `osx-arm64`, файл `.zip`), `UpdateSignature` (ECDSA P-256, DER), `UpdateKeys` (свой ключ Mac), `UpdateStore`,
`UpdateClient` (https, размер и SHA-256 при скачивании), `UpdateService` (состояния, проверка, скачивание, установка,
завершение), `UpdateBanner`. Своё для Mac: `UpdateEnvironment` (путь `.app` текущего процесса, сборка для разработки,
папка не для записи, AppTranslocation, образ диска), `AppZip` (распаковщик: байты zip читаются в память один раз — SHA-256
и распаковка по тем же байтам; только записи внутри `WaterCa7s PC Cleaner.app/`, без `..`, абсолютных путей и ссылок;
права из архива; служебные `__MACOSX/` и `._*` пропускаются), `BundleSwapper` (`.app.new` → проверки → `.app.old`,
`.new` на место, откат при сбое), запуск `open -n … --args --updated-from <версия>`.

**Tech Stack:** как в этапах 1–5; `System.IO.Compression`, `System.Security.Cryptography.ECDsa`.

**Спецификация:** «Автообновление», «CI» (`release.yml` — только после согласия пользователя на релиз).

## Global Constraints

- Всё из этапов 1–5. Тесты не трогают настоящую программу и сеть: подставной HTTP, временные папки, тестовая пара
  ключей; `[MacFact]` — распаковка и `codesign --verify` на временной копии.
- Закрытый ключ подписи обновлений — только в `%USERPROFILE%\WaterCa7sPcCleaner-signing\mac\` и (после согласия
  пользователя) в секрете `UPDATE_SIGNING_KEY`. В репозитории — только открытый.
- Сборка для разработки и режим скриншотов не проверяют и не ставят обновления.
- `release.yml`, тег и релиз — только с явного «да» пользователя.

## Задачи

### Task 1: Ключ, манифест, подпись

Пара ECDSA P-256 (openssl на ПК разработки), открытый — `tools/update-signing-public.pem` и `Core/Updates/UpdateKeys.cs`
(тест сверяет), закрытый — в резервную папку. `UpdateManifest`, `UpdateSignature`, `UpdateBanner` — копии Windows с
`osx-arm64`/`.zip`. `AppSettings.AutoUpdate` (по умолчанию да), `LastUpdateCheckUtc`. Тесты: подпись тестовым ключом,
подменённый файл, чужой ключ, неверные имя/ссылка/размер/хэш/версия.

### Task 2: Хранилище и клиент

`UpdateStore` (папка версии: zip, `update.json`, `.sig`; `state.json` со счётчиком неудач) и `UpdateClient` — копии
Windows. Тесты на подставном `HttpMessageHandler`: скачивание с проверкой размера и SHA-256, обрыв, лишние байты, только
https.

### Task 3: Распаковка и замена программы

`AppZip.Extract(bytes, sha256, size, targetApp)` и `BundleSwapper.Install(...)` с проверками `codesign --verify --deep
--strict`, bundle id и версии в Info.plist, требования подписи текущей программы (если она подписана сертификатом).
Тесты: zip slip (`../`, абсолютный путь), символическая ссылка, лишняя папка верхнего уровня, `__MACOSX`, права
исполняемого файла, неверный хэш, откат при сбое проверки; `[MacFact]` — настоящий `codesign` на ad-hoc копии.

### Task 4: Служба обновлений и окно

`UpdateEnvironment` (Mac), `UpdateService` — проверка при запуске в фоне (если включено), установка до окна,
`--updated-from` → удаление `.app.old` и `Updates/<версия>`; полоса в окне («Перезапустить сейчас», «Что нового»,
«Скачать вручную»); раздел «Обновления» в «Настройках». `tools/mac/make-update-manifest.sh` (openssl) + в CI: тестовый
ключ, манифест на собранном zip, проверка программой. Тесты модели и окна; CI; `.app` пользователю; затем — вопрос о
релизе 1.0.0 (`release.yml`, секрет `UPDATE_SIGNING_KEY`).
