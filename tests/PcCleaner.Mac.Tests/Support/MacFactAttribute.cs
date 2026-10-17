using System.Runtime.CompilerServices;
using Xunit;

namespace PcCleaner.Mac.Tests.Support;

/// <summary>A test that runs real macOS tools (read-only); skipped elsewhere.</summary>
public sealed class MacFactAttribute : FactAttribute
{
    public MacFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!OperatingSystem.IsMacOS())
            Skip = "только macOS";
    }
}
