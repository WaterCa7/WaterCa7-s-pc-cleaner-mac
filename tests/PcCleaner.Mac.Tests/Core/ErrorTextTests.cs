using System.ComponentModel;
using PcCleaner.Core;
using Xunit;

namespace PcCleaner.Mac.Tests.Core;

public class ErrorTextTests
{
    [Theory]
    [InlineData(typeof(UnauthorizedAccessException), "нет доступа — macOS не разрешила")]
    [InlineData(typeof(FileNotFoundException), "файл или папка не найдены")]
    [InlineData(typeof(DirectoryNotFoundException), "файл или папка не найдены")]
    [InlineData(typeof(TimeoutException), "превышено время ожидания")]
    [InlineData(typeof(OperationCanceledException), "операция отменена")]
    public void For_KnownExceptions_IsRussian(Type type, string expected)
        => Assert.StartsWith(expected, ErrorText.For((Exception)Activator.CreateInstance(type)!));

    [Fact]
    public void For_DiskFull_IsRecognisedByTheUnixMessage()
        => Assert.Equal("на диске недостаточно места", ErrorText.For(new IOException("No space left on device : '/x'")));

    [Fact]
    public void For_NotPermitted_IsAccessDenied()
        => Assert.Equal(ErrorText.AccessDenied, ErrorText.For(new IOException("Operation not permitted : '/Users/x/Library/Safari'")));

    [Fact]
    public void For_FailedStart_NamesTheCode()
        => Assert.Equal("не удалось запустить программу macOS (код 2)", ErrorText.For(new Win32Exception(2, "No such file or directory")));

    [Fact]
    public void For_CyrillicPath_InASystemMessage_IsStillTranslated()
        => Assert.Equal(ErrorText.AccessDenied,
            ErrorText.For(new UnauthorizedAccessException("Access to the path '/Users/Иван/Library/Safari' is denied.")));

    [Fact]
    public void For_RussianMessage_IsKept()
        => Assert.Equal("своя ошибка", ErrorText.For(new InvalidOperationException("своя ошибка.")));

    [Fact]
    public void Sentence_IsCapitalizedWithAPeriod()
        => Assert.Equal("Файл или папка не найдены.", ErrorText.Sentence(new FileNotFoundException()));
}
