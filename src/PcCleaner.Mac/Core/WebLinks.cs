namespace PcCleaner.Core;

/// <summary>Web pages the app opens in the browser on the user's request.</summary>
public static class WebLinks
{
    public static string Search(string query) => "https://www.google.com/search?q=" + Uri.EscapeDataString(query);

    /// <summary>The VirusTotal report of a file, found by its SHA-256 (the file itself is not uploaded).</summary>
    public static string VirusTotal(string sha256) => "https://www.virustotal.com/gui/file/" + sha256.ToLowerInvariant();
}
