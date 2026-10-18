using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using PcCleaner.Core;

namespace PcCleaner.Shell;

/// <summary>The macOS save panel and the clipboard of the main window.</summary>
public sealed class WindowReportOutput : IReportOutput
{
    private readonly TopLevel _window;

    public WindowReportOutput(TopLevel window) => _window = window;

    public async Task<string?> PickSavePathAsync(string suggestedName)
    {
        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Сохранить отчёт",
            SuggestedFileName = suggestedName,
            DefaultExtension = "html",
            FileTypeChoices = new[] { new FilePickerFileType("Веб-страница") { Patterns = new[] { "*.html" } } },
            SuggestedStartLocation = await _window.StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Documents),
        });
        return file?.TryGetLocalPath();
    }

    public async Task CopyTextAsync(string text)
    {
        if (_window.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }
}
