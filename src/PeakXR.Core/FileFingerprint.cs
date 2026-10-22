using System;
using System.IO;
using System.Security.Cryptography;

namespace PeakXR.Core;

/// <summary>
/// What a file looked like at one moment (present, size, last write, SHA-256), so a test run can prove it left one
/// of the player's files, such as the quicksave, exactly as it found it.
/// </summary>
public readonly struct FileFingerprint
{
    public readonly bool Exists;
    public readonly long Length;
    public readonly DateTime WriteUtc;
    public readonly string Sha256;

    private FileFingerprint(bool exists, long length, DateTime writeUtc, string sha256)
    {
        Exists = exists;
        Length = length;
        WriteUtc = writeUtc;
        Sha256 = sha256;
    }

    public static FileFingerprint Of(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            return new FileFingerprint(false, 0, default, "");
        using var sha = SHA256.Create();
        using var stream = info.OpenRead();
        var hash = BitConverter
            .ToString(sha.ComputeHash(stream))
            .Replace("-", "", StringComparison.Ordinal);
        return new FileFingerprint(true, info.Length, info.LastWriteTimeUtc, hash);
    }

    /// <summary>Same presence, size, last write and content.</summary>
    public bool Same(FileFingerprint other) =>
        Exists == other.Exists
        && Length == other.Length
        && WriteUtc == other.WriteUtc
        && string.Equals(Sha256, other.Sha256, StringComparison.Ordinal);

    public override string ToString() =>
        Exists
            ? $"{Length} bytes, written {WriteUtc:yyyy-MM-dd HH:mm:ss}Z, sha256 {Sha256.Substring(0, Math.Min(12, Sha256.Length))}"
            : "absent";
}
