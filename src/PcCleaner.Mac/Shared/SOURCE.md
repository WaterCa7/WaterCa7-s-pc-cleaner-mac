# Общие файлы с Windows-версией

Файлы в этой папке скопированы без изменений из репозитория Windows-версии
[WaterCa7/WaterCa7-s-pc-cleaner](https://github.com/WaterCa7/WaterCa7-s-pc-cleaner). Исправления в них переносятся
вручную в обе стороны; после переноса обновите коммит в таблице.

| Файл | Путь в Windows-репозитории | Коммит |
|---|---|---|
| `Format.cs` | `src/PcCleaner/Core/Format.cs` | `ad2a9fb` |
| `ObservableObject.cs` | `src/PcCleaner/Core/ObservableObject.cs` | `ad2a9fb` |
| `Health.cs` | `src/PcCleaner/Core/Health.cs` | `ad2a9fb` |
| `HealthScoring.cs` | `src/PcCleaner/Modules/Dashboard/HealthScoring.cs` | `ad2a9fb` |
| `SmartScanner.cs` | `src/PcCleaner/Modules/Dashboard/SmartScanner.cs` | `ad2a9fb` |
| `ScanSummary.cs` | `src/PcCleaner/Modules/Dashboard/ScanSummary.cs` | `ad2a9fb` |

Частичная копия вне этой папки: `Modules/Dashboard/ScoreModels.cs` — `ScoreTrend`, `ScoreDelta`, `ReclaimableSplit` из
`src/PcCleaner/Modules/Dashboard/ReportModels.cs` (строки 5–103, `ad2a9fb`); остальное в том файле зависит от WPF.

Адаптированные копии вне этой папки (меняются отдельно от Windows-версии):

- `Modules/Security/HostsRules.cs` — из `src/PcCleaner/Modules/Security/HostsRules.cs` (`c53ca62`): добавлены домены
  обновлений и проверок подписи Apple, в текстах «macOS» вместо «Windows».
- `Modules/Security/HostsFile.cs` — разбор строк и `SplitLines` из `src/PcCleaner/Modules/Security/HostsFile.cs`
  (`c53ca62`); запись файла на Mac идёт через окно пароля, поэтому вместо `RemoveLines` — `CommentOut` (новый текст).
- `Modules/Dashboard/ReportBuilder.cs` — из `src/PcCleaner/Modules/Dashboard/ReportBuilder.cs` (`c53ca62`): тексты про Mac,
  перевод строки `\n`. `Modules/Dashboard/ReportModels.cs` — `ReportModule`, `ReportIssue`, `ReportGroup`,
  `DashboardReport` из `ReportModels.cs` (`c53ca62`), сведения о компьютере заменены на `ReportMacInfo`.
