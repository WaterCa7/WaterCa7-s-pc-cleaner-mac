namespace PcCleaner.Core;

/// <summary>Where a report goes: a file the user picks and the clipboard (the window provides them; tests use a fake).</summary>
public interface IReportOutput
{
    /// <summary>The path the user chose in the save dialog, or null when they cancelled.</summary>
    Task<string?> PickSavePathAsync(string suggestedName);

    Task CopyTextAsync(string text);
}
