using System.IO.Compression;
using System.Security.Cryptography;

namespace Plugin.Maui.Spine.Controls.Avatar.Core;

/// <summary>The bounds a <c>.spineavatar</c> must stay within (spec §13). A trusted asset may get larger ones.</summary>
public sealed record AvatarArchiveLimits(
    long MaxCompressedBytes = 50 * 1024 * 1024,
    long MaxUncompressedBytes = 150 * 1024 * 1024,
    int MaxEntries = 512,
    long MaxEntryBytes = 32 * 1024 * 1024,
    double MaxCompressionRatio = 200)
{
    public static AvatarArchiveLimits Default { get; } = new();
}

/// <summary>
/// Reads a <c>.spineavatar</c> ZIP without trusting it: names are checked before anything is read,
/// every entry is read through a bound that does not believe the ZIP header, and each file must match
/// the manifest's SHA-256 and size. Nothing in the archive is ever executed or extracted to disk.
/// </summary>
public static class AvatarArchive
{
    public const string ManifestPath = "avatar.json";

    public static AvatarPackage Read(Stream stream, AvatarArchiveLimits? limits = null, CancellationToken cancellationToken = default)
    {
        var report = new AvatarValidationReport();
        var package = TryRead(stream, report, limits, cancellationToken);
        return package ?? throw new AvatarLoadException(report);
    }

    /// <summary>Reads and validates; returns null when a check failed, with the reasons in <paramref name="report"/>.</summary>
    public static AvatarPackage? TryRead(Stream stream, AvatarValidationReport report, AvatarArchiveLimits? limits = null, CancellationToken cancellationToken = default)
    {
        limits ??= AvatarArchiveLimits.Default;
        var files = ReadEntries(stream, report, limits, cancellationToken);
        if (files is null)
            return null;

        AvatarManifest manifest;
        try
        {
            manifest = AvatarJson.ReadManifest(files[ManifestPath]);
        }
        catch (AvatarFormatException e)
        {
            report.Fail("manifest", "schema", e.Message);
            return null;
        }

        if (!CheckIntegrity(manifest, files, report))
            return null;

        var package = new AvatarPackage(manifest, files, report);
        AvatarValidator.Validate(package, report);
        return report.HasFailures ? null : package;
    }

    private static Dictionary<string, byte[]>? ReadEntries(Stream stream, AvatarValidationReport report, AvatarArchiveLimits limits, CancellationToken cancellationToken)
    {
        const string area = "container";

        var archiveBytes = new MemoryStream();
        if (!CopyBounded(stream, archiveBytes, limits.MaxCompressedBytes, cancellationToken))
        {
            report.Fail(area, "size", $"the archive is larger than {limits.MaxCompressedBytes} bytes");
            return null;
        }
        archiveBytes.Position = 0;

        ZipArchive zip;
        try
        {
            zip = new ZipArchive(archiveBytes, ZipArchiveMode.Read);
        }
        catch (InvalidDataException e)
        {
            report.Fail(area, "zip", $"not a ZIP archive: {e.Message}");
            return null;
        }

        using (zip)
        {
            if (zip.Entries.Count > limits.MaxEntries)
            {
                report.Fail(area, "entries", $"{zip.Entries.Count} entries, more than {limits.MaxEntries}");
                return null;
            }

            // Names first: nothing is decompressed from an archive whose names are unsafe.
            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            long declared = 0;
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith('/'))
                    continue;

                if (UnsafeName(entry.FullName) is { } reason)
                {
                    report.Fail(area, "path", $"{entry.FullName}: {reason}");
                    return null;
                }
                if (IsSymbolicLink(entry))
                {
                    report.Fail(area, "path", $"{entry.FullName}: symbolic links are not allowed");
                    return null;
                }
                if (!seen.TryAdd(entry.FullName, entry.FullName))
                {
                    report.Fail(area, "path", $"{entry.FullName} collides with {seen[entry.FullName]} (names are compared ignoring case)");
                    return null;
                }
                if (entry.Length > limits.MaxEntryBytes)
                {
                    report.Fail(area, "size", $"{entry.FullName} is {entry.Length} bytes, more than {limits.MaxEntryBytes}");
                    return null;
                }
                if (entry.CompressedLength > 0 && entry.Length > 64 * 1024 && (double)entry.Length / entry.CompressedLength > limits.MaxCompressionRatio)
                {
                    report.Fail(area, "ratio", $"{entry.FullName} expands {entry.Length / entry.CompressedLength}×, more than {limits.MaxCompressionRatio}×");
                    return null;
                }
                declared += entry.Length;
            }

            if (declared > limits.MaxUncompressedBytes)
            {
                report.Fail(area, "size", $"{declared} bytes uncompressed, more than {limits.MaxUncompressedBytes}");
                return null;
            }

            if (!seen.ContainsKey(ManifestPath) || seen[ManifestPath] != ManifestPath)
            {
                report.Fail(area, "manifest", "no avatar.json at the root (the name is case-sensitive)");
                return null;
            }

            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith('/'))
                    continue;

                cancellationToken.ThrowIfCancellationRequested();
                using var source = entry.Open();
                var bytes = new MemoryStream((int)Math.Min(entry.Length, limits.MaxEntryBytes));
                // The header's length is not trusted: the read stops at the limit whatever it says.
                if (!CopyBounded(source, bytes, limits.MaxEntryBytes, cancellationToken))
                {
                    report.Fail(area, "size", $"{entry.FullName} decompresses past {limits.MaxEntryBytes} bytes");
                    return null;
                }
                total += bytes.Length;
                if (total > limits.MaxUncompressedBytes)
                {
                    report.Fail(area, "size", $"the archive decompresses past {limits.MaxUncompressedBytes} bytes");
                    return null;
                }
                files[entry.FullName] = bytes.ToArray();
            }

            report.Pass(area, "zip", $"{files.Count} files, {total} bytes");
            return files;
        }
    }

    private static bool CheckIntegrity(AvatarManifest manifest, Dictionary<string, byte[]> files, AvatarValidationReport report)
    {
        const string area = "container";
        var ok = true;
        var listed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in manifest.Files)
        {
            if (file.Path == ManifestPath)
            {
                report.Fail(area, "files", "avatar.json lists itself in files");
                ok = false;
                continue;
            }
            if (!listed.Add(file.Path))
            {
                report.Fail(area, "files", $"{file.Path} is listed twice");
                ok = false;
                continue;
            }
            if (!files.TryGetValue(file.Path, out var bytes))
            {
                report.Fail(area, "files", $"{file.Path} is listed but not in the archive");
                ok = false;
                continue;
            }
            if (bytes.LongLength != file.Bytes)
            {
                report.Fail(area, "files", $"{file.Path} is {bytes.LongLength} bytes, the manifest says {file.Bytes}");
                ok = false;
                continue;
            }

            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (hash != file.Sha256)
            {
                report.Fail(area, "sha256", $"{file.Path} hashes to {hash}, the manifest says {file.Sha256}");
                ok = false;
            }
        }

        foreach (var path in files.Keys)
        {
            if (path != ManifestPath && !listed.Contains(path))
            {
                report.Fail(area, "files", $"{path} is in the archive but not listed in files");
                ok = false;
            }
        }

        if (ok)
            report.Pass(area, "integrity", $"{listed.Count} files match their SHA-256 and size");
        return ok;
    }

    /// <summary>Why a ZIP entry name is unsafe, or null when it is a plain relative forward-slash path.</summary>
    public static string? UnsafeName(string name)
    {
        if (name.Length == 0)
            return "empty name";
        if (name.StartsWith('/'))
            return "absolute path";
        if (name.Contains('\\'))
            return "backslash in path";
        if (name.Contains(':'))
            return "drive prefix or colon in path";
        if (name.Contains('\0'))
            return "NUL in path";

        foreach (var segment in name.Split('/'))
        {
            if (segment is "" or "." or "..")
                return $"'{segment}' segment in path";
        }
        return null;
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
    {
        // Unix file type in the high 16 bits of the external attributes; 0xA000 is a symlink.
        var mode = (entry.ExternalAttributes >> 16) & 0xF000;
        return mode == 0xA000;
    }

    private static bool CopyBounded(Stream source, Stream destination, long limit, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            total += read;
            if (total > limit)
                return false;
            destination.Write(buffer, 0, read);
        }
        return true;
    }
}

public sealed class AvatarLoadException(AvatarValidationReport report)
    : Exception("The avatar did not load: " + string.Join("; ", report.Failures.Select(f => $"{f.Area}/{f.Name}: {f.Detail}")))
{
    public AvatarValidationReport Report { get; } = report;
}
