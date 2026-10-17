using System.Runtime.InteropServices;

namespace PcCleaner.Core;

/// <summary>What NSFileManager answered to «move to the Trash».</summary>
public sealed record NativeTrashResult(bool Ok, string? Error, bool PermissionDenied);

/// <summary>
/// The few Foundation calls the app needs, made through the Objective-C runtime (macOS only). Every call runs inside its
/// own autorelease pool. On other systems <see cref="IsAvailable"/> is false and the calls report that.
/// </summary>
public static class MacNative
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string FoundationPath = "/System/Library/Frameworks/Foundation.framework/Foundation";

    // NSCocoaErrorDomain: NSFileReadNoPermissionError, NSFileWriteNoPermissionError.
    private const long ReadNoPermission = 257;
    private const long WriteNoPermission = 513;

    private static readonly Lazy<IntPtr> Foundation = new(
        () => OperatingSystem.IsMacOS() && NativeLibrary.TryLoad(FoundationPath, out var handle) ? handle : IntPtr.Zero);

    public static bool IsAvailable => Foundation.Value != IntPtr.Zero;

    /// <summary>Moves a file or folder to the Trash of its volume (Finder's «Вернуть» works).</summary>
    public static NativeTrashResult TrashItem(string path)
    {
        if (!IsAvailable)
            return new NativeTrashResult(false, "Корзина есть только в macOS", false);
        using var pool = new AutoreleasePool();
        var manager = Send(Class("NSFileManager"), Sel("defaultManager"));
        var ok = SendTrash(manager, Sel("trashItemAtURL:resultingItemURL:error:"), FileUrl(path), IntPtr.Zero, out var error);
        if (ok)
            return new NativeTrashResult(true, null, false);
        if (error == IntPtr.Zero)
            return new NativeTrashResult(false, null, false);
        var code = SendLong(error, Sel("code"));
        return new NativeTrashResult(false, ToManaged(Send(error, Sel("localizedDescription"))), code is ReadNoPermission or WriteNoPermission);
    }

    /// <summary>
    /// Free space of the volume including what macOS can purge by itself (caches, local snapshots) — the "available" of
    /// Finder. Null when unknown.
    /// </summary>
    public static long? AvailableForImportantUsage(string path)
    {
        if (!IsAvailable)
            return null;
        using var pool = new AutoreleasePool();
        var keyAddress = NativeLibrary.GetExport(Foundation.Value, "NSURLVolumeAvailableCapacityForImportantUsageKey");
        var key = Marshal.ReadIntPtr(keyAddress);
        if (!SendGetResource(FileUrl(path), Sel("getResourceValue:forKey:error:"), out var value, key, out _) || value == IntPtr.Zero)
            return null;
        var bytes = SendLong(value, Sel("longLongValue"));
        return bytes > 0 ? bytes : null;
    }

    /// <summary>Managed → NSString → managed (tests the bridge, Cyrillic included).</summary>
    internal static string? RoundTrip(string text)
    {
        if (!IsAvailable)
            return null;
        using var pool = new AutoreleasePool();
        return ToManaged(NSString(text));
    }

    private static IntPtr FileUrl(string path) => Send(Class("NSURL"), Sel("fileURLWithPath:"), NSString(path));

    private static IntPtr NSString(string text)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(text);
        try
        {
            return Send(Class("NSString"), Sel("stringWithUTF8String:"), utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    private static string? ToManaged(IntPtr nsString)
        => nsString == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(Send(nsString, Sel("UTF8String")));

    private static IntPtr Class(string name) => objc_getClass(name);

    private static IntPtr Sel(string name) => sel_registerName(name);

    private readonly struct AutoreleasePool : IDisposable
    {
        private readonly IntPtr _pool;

        public AutoreleasePool() => _pool = objc_autoreleasePoolPush();

        public void Dispose() => objc_autoreleasePoolPop(_pool);
    }

    [DllImport(ObjC)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjC)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjC)]
    private static extern IntPtr objc_autoreleasePoolPush();

    [DllImport(ObjC)]
    private static extern void objc_autoreleasePoolPop(IntPtr pool);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern long SendLong(IntPtr receiver, IntPtr selector);

    // - (BOOL)trashItemAtURL:(NSURL *)url resultingItemURL:(NSURL **)outResultingURL error:(NSError **)error
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendTrash(IntPtr receiver, IntPtr selector, IntPtr url, IntPtr resultingUrl, out IntPtr error);

    // - (BOOL)getResourceValue:(id *)value forKey:(NSURLResourceKey)key error:(NSError **)error
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendGetResource(IntPtr receiver, IntPtr selector, out IntPtr value, IntPtr key, out IntPtr error);
}
