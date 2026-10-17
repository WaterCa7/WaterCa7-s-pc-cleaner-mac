using PcCleaner.Core;
using PcCleaner.Modules.Startup;

namespace PcCleaner.Modules.Settings;

/// <summary>A row of «Резервные копии»: what was changed, when, where it came from.</summary>
public sealed class BackupItem
{
    public BackupItem(BackupRecord record, Func<BackupItem, Task> restore, Func<BackupItem, Task> show)
    {
        Record = record;
        RestoreCommand = new AsyncRelayCommand(() => restore(this));
        ShowCommand = new AsyncRelayCommand(() => show(this));
    }

    public BackupRecord Record { get; }

    public string Title => Record.Title;

    /// <summary>"17.10.2026 21:40 — запись отключена".</summary>
    public string WhatText => $"{Format.DateTime(Record.Time)} — {BackupRestorer.Describe(Record)}";

    public string FromText => Record.OriginalPath ?? "";

    public AsyncRelayCommand RestoreCommand { get; }

    public AsyncRelayCommand ShowCommand { get; }
}
