using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media;
using PcCleaner.Core;

namespace PcCleaner.Shell;

/// <summary>
/// The app's message box: «Да»/«Нет» for questions (Escape and closing the window mean «Нет», and «Нет» has the focus),
/// «ОК» for messages. App.axaml.cs plugs it into <see cref="Dialogs"/>.
/// </summary>
public partial class DialogWindow : Window
{
    public DialogWindow()
    {
        InitializeComponent();
    }

    private DialogWindow(DialogRequest request, bool question)
        : this()
    {
        Title = request.Title;
        TitleText.Text = request.Title;
        MessageText.Text = request.Message;
        var (icon, brush) = request.Kind switch
        {
            DialogKind.Warning => ("Icon.Warning", "WarningBrush"),
            DialogKind.Question => ("Icon.Question", "InfoBrush"),
            DialogKind.Error => ("Icon.Error", "DangerBrush"),
            _ => ("Icon.Info", "AccentBrush"),
        };
        KindIcon.Data = ThemeResources.Find(icon) as Geometry;
        KindIcon.Foreground = ThemeResources.Find(brush) as IBrush;
        if (question)
        {
            Opened += (_, _) => NoButton.Focus();
        }
        else
        {
            NoButton.IsVisible = false;
            YesButton.Content = "ОК";
            YesButton.IsCancel = true;
            Opened += (_, _) => YesButton.Focus();
        }
    }

    /// <summary>True only when «Да» was pressed. Without a visible main window the answer is «Нет».</summary>
    public static async Task<bool> ConfirmAsync(DialogRequest request)
    {
        if (VisibleMainWindow() is not { } owner)
        {
            Log.Warn($"Вопрос «{request.Title}» не показан: нет окна программы — ответ «Нет»");
            return false;
        }
        return await new DialogWindow(request, question: true).ShowDialog<bool>(owner);
    }

    public static async Task ShowAsync(DialogRequest request)
    {
        if (VisibleMainWindow() is not { } owner)
        {
            Log.Info($"[{request.Title}] {request.Message}");
            return;
        }
        await new DialogWindow(request, question: false).ShowDialog<bool>(owner);
    }

    private static Window? VisibleMainWindow()
        => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is { IsVisible: true } w ? w : null;

    private void OnYes(object? sender, RoutedEventArgs e) => Close(NoButton.IsVisible);

    private void OnNo(object? sender, RoutedEventArgs e) => Close(false);
}
