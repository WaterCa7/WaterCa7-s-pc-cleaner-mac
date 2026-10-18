namespace PcCleaner.Core.Updates;

/// <summary>
/// Public half of the Mac release signing key — the same as tools/update-signing-public.pem (a test compares them). The
/// private half exists only as the GitHub Actions secret UPDATE_SIGNING_KEY and in the owner's backup. It is not the key
/// of the Windows version: a leak of one does not affect the other program.
/// </summary>
public static class UpdateKeys
{
    public const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE0AC1eu7Ut0utPvVKKBfoDM+kiqpe
        NSTiEQcOAUjBhMXn/R+Xk1Rotj3xmSHYAfjWSWymYMu8bg3exFtChy2Lpw==
        -----END PUBLIC KEY-----
        """;
}
