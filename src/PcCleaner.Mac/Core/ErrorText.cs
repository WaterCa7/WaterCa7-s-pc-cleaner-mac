using System.ComponentModel;
using System.Security;

namespace PcCleaner.Core;

/// <summary>
/// Russian text for exceptions shown to the user. The .NET runtime has no Russian resources, so ex.Message of common
/// errors ("Access to the path ... is denied.") would appear in English inside Russian dialogs. Messages that are already
/// Russian (the app's own) are kept as is. The raw message and stack trace still go to the log file (<see cref="Log.Error"/>).
/// </summary>
public static class ErrorText
{
    public const string AccessDenied =
        "нет доступа — macOS не разрешила это действие (может понадобиться полный доступ к диску или пароль администратора)";

    /// <summary>Short lower-case phrase without a final period, e.g. "файл или папка не найдены".</summary>
    public static string For(Exception? ex)
    {
        if (ex is null)
            return "неизвестная ошибка";
        if (ex is AggregateException { InnerExceptions.Count: 1 } aggregate)
            return For(aggregate.InnerExceptions[0]);
        if (ex is System.Reflection.TargetInvocationException { InnerException: { } inner })
            return For(inner);

        var message = ex.Message?.Trim() ?? "";
        if (HasCyrillic(message))
            return message.TrimEnd('.');

        return ex switch
        {
            OperationCanceledException => "операция отменена",
            UnauthorizedAccessException or SecurityException => AccessDenied,
            FileNotFoundException or DirectoryNotFoundException => "файл или папка не найдены",
            PathTooLongException => "слишком длинный путь к файлу",
            IOException when message.Contains("No space left on device", StringComparison.OrdinalIgnoreCase) => "на диске недостаточно места",
            IOException when message.Contains("Operation not permitted", StringComparison.OrdinalIgnoreCase) => AccessDenied,
            IOException => "не удалось прочитать или записать файл",
            Win32Exception w => $"не удалось запустить программу macOS (код {w.NativeErrorCode})",
            TimeoutException => "превышено время ожидания",
            OutOfMemoryException or InsufficientExecutionStackException => "не хватило памяти",
            _ => "непредвиденная ошибка — подробности записаны в журнал",
        };
    }

    /// <summary>The same as a sentence for a dialog: capitalized, with a final period.</summary>
    public static string Sentence(Exception? ex)
    {
        var text = For(ex);
        if (text.Length == 0)
            return text;
        text = char.ToUpper(text[0], Format.Ru) + text[1..];
        return text.EndsWith('.') || text.EndsWith('!') || text.EndsWith('?') || text.EndsWith('…') ? text : text + ".";
    }

    private static bool HasCyrillic(string text)
    {
        foreach (var c in text)
        {
            if (c is >= 'Ѐ' and <= 'ӿ')
                return true;
        }
        return false;
    }
}
