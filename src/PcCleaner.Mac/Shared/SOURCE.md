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
