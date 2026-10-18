# Этап 5 «Безопасность и сеть» и HTML-отчёт — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** раздел «Безопасность и сеть» по таблице спецификации (SIP, Gatekeeper, FileVault, брандмауэр, обновления,
версия macOS, XProtect, вход без пароля и гость, удалённый доступ, профили, прокси, DNS, hosts, Wi-Fi) с объяснениями,
кнопками настроек и исправлениями через окно пароля (с копией прежних значений); находки для «Главной»; HTML-отчёт и
текст для буфера обмена на «Главной».

**Architecture:** каждая проверка — чистый парсер вывода утилиты (образцы с машин CI) + чистое правило → `SecurityCheck`
(статус, текст, действие). `SecurityService` собирает проверки через `ICommandRunner` (медленный `softwareupdate -l` —
отдельно, в фоне). Исправления — `AdminPrompt` (команды как аргументы), перед изменением сети и hosts — копия в
`BackupStore` (новые виды: настройки сети в JSON, файл hosts), восстановление — в «Настройках». Отчёт — адаптированная
копия `ReportBuilder` Windows-версии (чистый код: модель → HTML/текст).

**Tech Stack:** как в этапах 1–4.

**Спецификация:** «Безопасность и сеть», «Главная» (отчёт), «Сервисы Core» (`Backups`). Уточнения этапа:
(1) «три последние версии macOS» — по упорядоченному ряду (…, 14, 15, 26, 27, …); самая новая — наибольшая из таблицы в
коде, версии этого Mac и версий, которые предлагает `softwareupdate`; (2) DNS — находка только для адресов, заданных
вручную (`networksetup -getdnsservers`), которые не частные (сеть дома/роутер) и не из списка известных публичных; DNS
от роутера/провайдера (DHCP) и от VPN не находка; (3) прокси — любой включённый HTTP/HTTPS/SOCKS или PAC (в тексте —
что так делают и VPN/корпоративные программы); (4) правила hosts — копия Windows `HostsRules` с доменами обновлений и
проверок подписи Apple (`swscan`, `swdist`, `swcdn`, `mesu`, `gdmf`, `updates.cdn-apple.com`, `ocsp*.apple.com`,
`crl.apple.com`, `valid.apple.com`, `xp.apple.com`) и словами «macOS» вместо «Windows»; (5) исправления hosts —
закомментировать строки: новый текст пишется во временный файл программы, затем `cp` в `/etc/hosts` через окно пароля и
сброс кэша DNS.

## Global Constraints

- Всё из этапов 1–4. Тесты не меняют систему: `spctl`, `socketfilterfw`, `networksetup`, `cp` — только в подставном
  исполнителе; `[MacFact]` только читают (`csrutil status`, `spctl --status`, `fdesetup status`, `socketfilterfw --get…`,
  `scutil`, `networksetup -get…`, `/etc/hosts`, `system_profiler`).
- Исправления с паролем не входят в «Исправить найденное» (`AutoFixSafe = false`), у каждого — своя кнопка и
  подтверждение со списком команд.
- Перед изменением прокси, DNS, hosts — копия прежних значений/файла; без копии — ничего не меняется.
- Имя сети Wi-Fi нигде не сохраняется (ни в журнале, ни в отчёте).
- Анализ для «Главной» не вызывает запросов разрешений macOS; `softwareupdate -l` — с таймаутом 60 с.

## Задачи

### Task 1: Образцы и разбор защиты macOS

`capture-fixtures.sh` (сделано до плана): `xprotect version`, дата `XProtect.bundle`, `profiles status -type
enrollment`, Wi-Fi, общий экран, порядок служб и для каждой службы DNS/прокси/PAC/WPAD/адреса. Образцы →
`Fixtures/security/`, `Fixtures/network/`. `Modules/Security/SecurityParsers.cs`: `Sip(text)`, `Gatekeeper(text)`,
`FileVault(text)`, `Firewall(text)`, `Stealth(text)` (оба формата: «Firewall stealth mode is off» и «Stealth mode
disabled»), `SoftwareUpdates(text)` → список (`Title`, `Version`, `Recommended`, `Restart`, `IsSecurity`),
`UpdatePrefs(plist)` (автопроверка, ответы безопасности; нет ключа — включено), `LoginWindow(plist)` (автовход, гость),
`XProtect(version, date)`, `Enrollment(text)` (MDM). `MacOsVersions.Supported(current, offered)`. Тесты на образцах обеих
машин CI и своих («включено», «пусто», «ошибка»).

### Task 2: Профили и сеть

`ProfileParsers.Parse(json)` → `ConfigProfile(Name, Organization, Payloads)` (рекурсивно по `_items`, терпимо к
незнакомым полям) и классификация полезной нагрузки: MDM, прокси, корневой сертификат, настройки браузера, прочее.
`NetworkParsers`: `ScutilProxy(text)` (словарь `scutil`), `NetworksetupProxy(text)` (`Enabled/Server/Port`), `Pac(text)`,
`DnsServers(text)` («There aren't any DNS Servers set…» → пусто), `ServiceInfo(text)` (Router), `Services(text)` (без
строки-пояснения, `*` — выключена), `Wifi(json)` → режим защиты текущей сети (без имени). `DnsRules` (частные адреса,
известные публичные DNS, IPv4 и IPv6). `HostsFile` (разбор, как в Windows) + `HostsRules` (адаптированная копия).
Тесты.

### Task 3: Сервис проверок

`SecurityCheck(Id, Group, Title, Status, Summary, Details, Action?)`, `SecurityService.CheckAsync(ct)` (всё, кроме
обновлений) и `UpdatesAsync(ct)` (`softwareupdate -l`, 60 с); ошибки утилит → статус «нет данных» с причиной. Тесты на
подставном исполнителе с образцами.

### Task 4: Исправления и копии

`SecurityFixes`: Gatekeeper (`spctl --global-enable`), брандмауэр и невидимость (`socketfilterfw --setglobalstate on`,
`--setstealthmode on`), прокси (копия значений всех служб → `networksetup -set…state <служба> off`), DNS (копия →
`-setdnsservers <служба> Empty`), hosts (копия файла → новый текст во временный файл → `cp` + `dscacheutil -flushcache` +
`killall -HUP mDNSResponder`). `BackupStore`: `SaveJson(kind, title, values)`, `SaveFile(kind, path)`; `BackupRestorer`
— восстановление настроек сети (`networksetup -setwebproxy … on/off`, `-setdnsservers …`) и hosts (через окно пароля).
Тесты: команды и порядок, отмена в окне пароля, копия не сохранилась — ничего.

### Task 5: Находки для «Главной»

`SecurityContributor` (модуль «Безопасность и сеть», страница `Security`) по таблице спецификации; исправления с паролем
— `FixAsync` без `AutoFixSafe`. Тесты.

### Task 6: Страница, отчёт, CI

`SecurityViewModel` (Key `Security`, «Безопасность и сеть», Icon `Shield`): группы «Защита macOS», «Вход и общий
доступ», «Профили», «Сеть»; у каждой проверки — статус, объяснение, кнопка (исправить через пароль / открыть
настройки / инструкция); строки hosts с галочками; обновления — в фоне. «Главная»: «Сохранить отчёт» (HTML, выбор файла)
и «Скопировать отчёт» (текст) — `ReportBuilder` (адаптированная копия) с разделом «Сведения о Mac». Тесты модели (без
«Да» ничего не меняется), отчёта (экранирование, без имени сети Wi-Fi), окна, снимки; CI; `.app` пользователю.
