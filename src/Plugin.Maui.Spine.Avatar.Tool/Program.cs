using System.Globalization;
using System.Text;
using Plugin.Maui.Spine.Avatar.Tool;
using Plugin.Maui.Spine.Controls.Avatar;
using SkiaSharp;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

try
{
    return args switch
    {
        ["inspect", var file] => Commands.Inspect(file),
        ["validate", var file, .. var rest] => Commands.Validate(file, Options.Parse(rest)),
        ["pack", var file, .. var rest] => Commands.Pack(file, Options.Parse(rest)),
        ["strip", var file, .. var rest] => Commands.Strip(file, Options.Parse(rest)),
        ["render-sheet", var file, .. var rest] => Commands.RenderSheet(file, Options.Parse(rest)),
        ["report", .. var files] when files.Length > 0 => Commands.Report(files),
        _ => Commands.Usage(),
    };
}
catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
{
    Console.Error.WriteLine($"spine-avatar: {e.Message}");
    return 2;
}

internal sealed record Options(string? Output, string? Profile, string? PosterLight, string? PosterDark, int Size, bool Strip, string? AssetVersion = null, IReadOnlyDictionary<string, string>? Voices = null)
{
    public static Options Parse(string[] args)
    {
        string? output = null, profile = null, light = null, dark = null, assetVersion = null;
        var size = 512;
        var strip = false;
        var voices = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
            switch (args[i])
            {
                case "-o" or "--output": output = Next(); break;
                case "--profile": profile = Next(); break;
                case "--poster-light": light = Next(); break;
                case "--poster-dark": dark = Next(); break;
                case "--size": size = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--strip": strip = true; break;
                case "--asset-version":
                    assetVersion = Next();
                    if (!System.Text.RegularExpressions.Regex.IsMatch(assetVersion, @"^\d+\.\d+\.\d+$"))
                        throw new ArgumentException($"--asset-version takes major.minor.patch, not {assetVersion}.");
                    break;
                case "--voice":
                    var pair = Next().Split('=', 2);
                    if (pair.Length != 2 || !System.Text.RegularExpressions.Regex.IsMatch(pair[0], "^[a-z][a-z0-9-]{0,31}$") || pair[1].Length is 0 or > 64)
                        throw new ArgumentException("--voice takes provider=voice, such as openai=marin.");
                    voices[pair[0]] = pair[1];
                    break;
                default: throw new ArgumentException($"Unknown option {args[i]}.");
            }
        }
        return new Options(output, profile, light, dark, size, strip, assetVersion, voices.Count > 0 ? voices : null);
    }
}

internal static class Commands
{
    // Under this, a poster is a placeholder rather than a picture.
    private const int PosterMinimum = 128;
    private const long BundleGuide = 2 * 1024 * 1024;

    public static int Usage()
    {
        Console.Error.WriteLine("""
            spine-avatar <command> <file.spineavatar> [options]

              inspect <file>                         what the package holds
              validate <file> [--profile shipping]   the format's checks; shipping adds real posters and no source/
              pack <file> -o <out> [--strip] [--poster-light <png> --poster-dark <png>] [--asset-version 1.2.0]
                   [--voice openai=marin ...]        the defaults applied, capabilities computed, posters rendered,
                                                     the hash table rewritten; a new release takes a higher
                                                     assetVersion, which apps compare to update a downloaded copy;
                                                     --voice names the avatar's voice per provider
              strip <file> -o <out>                  source/ removed, the hash table rewritten
              render-sheet <file> -o <dir> [--size 512]
                                                     posters and the state and expression sheets as PNG
              report <file>...                       one Markdown row per package
            """);
        return 2;
    }

    public static int Inspect(string file)
    {
        var (package, report) = AvatarFiles.Read(file);
        if (package is null)
            return Fail(file, report);

        var m = package.Manifest;
        Console.WriteLine($"{m.DisplayName} ({m.Id}) {m.AssetVersion}, profile {m.Profile}, {Size(new FileInfo(file).Length)}");
        foreach (var r in m.Representations)
            Console.WriteLine($"  representation {r.Id}: {r.Renderer}/{r.Format} {r.Model} ({Size(package.GetFile(r.Model).Length)}), capabilities {string.Join(", ", AvatarCapabilities.Compute(package, r))}");
        foreach (var (label, path) in new[] { ("poster light", m.Posters.Light), ("poster dark", m.Posters.Dark) })
            Console.WriteLine($"  {label}: {path} {PosterSize(package, path)}");
        foreach (var path in package.FilePaths.Order(StringComparer.Ordinal))
            Console.WriteLine($"  {path,-40} {Size(package.GetFile(path).Length),10}");
        Console.WriteLine($"  validation: {report.Summary()}");
        return 0;
    }

    public static int Validate(string file, Options options)
    {
        var (package, report) = AvatarFiles.Read(file);
        if (package is not null && options.Profile is { } profile)
        {
            if (profile != "shipping")
                throw new ArgumentException($"Unknown profile {profile}; the one there is is shipping.");
            Shipping(package, new FileInfo(file).Length, report);
        }

        foreach (var check in report.Checks.Where(c => c.Status != AvatarCheckStatus.Pass))
            Console.WriteLine($"{check.Status,-11} {check.Area}/{check.Name}: {check.Detail}");
        Console.WriteLine($"{Path.GetFileName(file)}: {report.Summary()}");
        return package is null || report.HasFailures ? 1 : 0;
    }

    public static int Pack(string file, Options options)
    {
        var output = options.Output ?? throw new ArgumentException("pack needs -o <out.spineavatar>.");
        var (package, report) = AvatarFiles.Read(file);
        if (package is null || report.HasFailures)
            return Fail(file, report);

        var files = AvatarFiles.Contents(package);
        if (options.Strip)
            RemoveSource(files);

        foreach (var r in package.Manifest.Representations)
        {
            if (r.Bindings is not { } bindings)
                continue;
            if (AvatarSlimming.Bindings(package, r) is { } slim)
                files[bindings] = Encoding.UTF8.GetBytes(slim.ToJsonString(new() { WriteIndented = true }));
            else
                files.Remove(bindings);
        }

        var posters = package.Manifest.Posters;
        if (options.PosterLight is not null || options.PosterDark is not null)
        {
            files[posters.Light] = File.ReadAllBytes(options.PosterLight ?? throw new ArgumentException("--poster-light goes with --poster-dark."));
            files[posters.Dark] = File.ReadAllBytes(options.PosterDark ?? throw new ArgumentException("--poster-dark goes with --poster-light."));
            Console.WriteLine($"posters: from {options.PosterLight} and {options.PosterDark}");
        }
        else if (AvatarPainter.Create(package, out var reason) is { } painter)
        {
            using (painter)
            {
                files[posters.Light] = Png(painter.Draw(options.Size, dark: false));
                files[posters.Dark] = Png(painter.Draw(options.Size, dark: true));
            }
            Console.WriteLine($"posters: rendered at {options.Size} px from {painter.Representation}");
        }
        else
        {
            Console.WriteLine($"posters: kept as they were, because {reason}");
        }

        var manifest = AvatarSlimming.Manifest(package);
        if (options.AssetVersion is { } version)
        {
            manifest["assetVersion"] = version;
            Console.WriteLine($"assetVersion: {package.Manifest.AssetVersion} -> {version}");
        }
        if (options.Voices is { } voices)
        {
            var named = manifest["voices"] as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject();
            foreach (var (provider, voice) in voices)
                named[provider] = voice;
            manifest["voices"] = named;
            Console.WriteLine($"voices: {string.Join(", ", named.Select(p => $"{p.Key}={p.Value}"))}");
        }
        AvatarFiles.Write(output, manifest, files);
        return Check(file, output);
    }

    public static int Strip(string file, Options options)
    {
        var output = options.Output ?? throw new ArgumentException("strip needs -o <out.spineavatar>.");
        var (package, report) = AvatarFiles.Read(file);
        if (package is null || report.HasFailures)
            return Fail(file, report);

        var files = AvatarFiles.Contents(package);
        var removed = RemoveSource(files);
        AvatarFiles.Write(output, AvatarFiles.Manifest(package), files);
        Console.WriteLine($"removed {removed} file(s) under {AvatarFiles.Source}");
        return Check(file, output);
    }

    public static int RenderSheet(string file, Options options)
    {
        var output = options.Output ?? throw new ArgumentException("render-sheet needs -o <directory>.");
        var (package, report) = AvatarFiles.Read(file);
        if (package is null || report.HasFailures)
            return Fail(file, report);
        if (AvatarPainter.Create(package, out var reason) is not { } painter)
        {
            Console.Error.WriteLine($"{Path.GetFileName(file)}: nothing to draw with, because {reason}.");
            return 1;
        }

        Directory.CreateDirectory(output);
        var id = package.Manifest.Id;
        using (painter)
        {
            void Save(string name, SKImage image)
            {
                File.WriteAllBytes(Path.Combine(output, name), Png(image));
                Console.WriteLine(Path.Combine(output, name));
            }

            Save($"{id}-poster-light.png", painter.Draw(options.Size, dark: false));
            Save($"{id}-poster-dark.png", painter.Draw(options.Size, dark: true));
            var cell = Math.Max(128, options.Size / 2);
            Save($"{id}-states.png", painter.Sheet([.. Enum.GetValues<AvatarState>().Select(s => (s.ToString(), s, AvatarExpression.Neutral))], cell, dark: false));
            Save($"{id}-expressions.png", painter.Sheet([.. Enum.GetValues<AvatarExpression>().Select(e => (e.ToString(), AvatarState.Idle, e))], cell, dark: false));
        }
        return 0;
    }

    public static int Report(string[] files)
    {
        Console.WriteLine("| Package | Size | Representations | Posters | source/ | Validation | Shipping |");
        Console.WriteLine("| --- | --- | --- | --- | --- | --- | --- |");
        var failed = false;
        foreach (var file in files)
        {
            var (package, report) = AvatarFiles.Read(file);
            if (package is null)
            {
                failed = true;
                Console.WriteLine($"| {Path.GetFileName(file)} | | | | | does not load: {report.Summary()} | |");
                continue;
            }
            var length = new FileInfo(file).Length;
            var shipping = new AvatarValidationReport();
            Shipping(package, length, shipping);
            var m = package.Manifest;
            failed |= report.HasFailures;
            Console.WriteLine($"| {m.DisplayName} ({m.Id}) | {Size(length)} | {string.Join(", ", m.Representations.Select(r => $"{r.Renderer}/{r.Format}"))} | " +
                $"{PosterSize(package, m.Posters.Light)} | {(package.FilePaths.Any(p => p.StartsWith(AvatarFiles.Source, StringComparison.Ordinal)) ? "yes" : "no")} | " +
                $"{report.Summary()} | {(shipping.HasFailures ? string.Join("; ", shipping.Checks.Where(c => c.Status == AvatarCheckStatus.Fail).Select(c => c.Name)) : shipping.Checks.Any(c => c.Status == AvatarCheckStatus.Warning) ? "warnings" : "ready")} |");
        }
        return failed ? 1 : 0;
    }

    /// <summary>The shipping profile: what a package needs beyond the format before it goes to users.</summary>
    internal static void Shipping(AvatarPackage package, long length, AvatarValidationReport report)
    {
        foreach (var (label, path) in new[] { ("poster-light", package.Manifest.Posters.Light), ("poster-dark", package.Manifest.Posters.Dark) })
        {
            if (!package.TryGetFile(path, out var bytes))
                continue;
            using var codec = SKCodec.Create(new SKMemoryStream(bytes.ToArray()));
            if (codec is null)
                report.Fail("shipping", label, $"{path} is not an image.");
            else if (codec.Info.Width < PosterMinimum || codec.Info.Height < PosterMinimum)
                report.Fail("shipping", label, $"{path} is {codec.Info.Width}×{codec.Info.Height}, a placeholder; spine-avatar pack renders it.");
            else
                report.Pass("shipping", label, $"{codec.Info.Width}×{codec.Info.Height}");
        }

        var sources = package.FilePaths.Count(p => p.StartsWith(AvatarFiles.Source, StringComparison.Ordinal));
        if (sources > 0)
            report.Fail("shipping", "source", $"{sources} file(s) under {AvatarFiles.Source}; spine-avatar strip removes them.");
        else
            report.Pass("shipping", "source");

        if (length > BundleGuide)
            report.Warn("shipping", "size", $"{Size(length)}: download it rather than bundle it (about 2 MB is the guide for a bundled avatar).");
        else
            report.Pass("shipping", "size", Size(length));
    }

    private static int RemoveSource(SortedDictionary<string, byte[]> files)
    {
        var source = files.Keys.Where(p => p.StartsWith(AvatarFiles.Source, StringComparison.Ordinal)).ToList();
        foreach (var path in source)
            files.Remove(path);
        return source.Count;
    }

    private static int Check(string input, string output)
    {
        var (package, report) = AvatarFiles.Read(output);
        Console.WriteLine($"{Path.GetFileName(output)}: {Size(new FileInfo(input).Length)} → {Size(new FileInfo(output).Length)}, {report.Summary()}");
        foreach (var check in report.Checks.Where(c => c.Status is AvatarCheckStatus.Fail or AvatarCheckStatus.Warning))
            Console.WriteLine($"{check.Status,-11} {check.Area}/{check.Name}: {check.Detail}");
        return package is null || report.HasFailures ? 1 : 0;
    }

    private static int Fail(string file, AvatarValidationReport report)
    {
        Console.Error.WriteLine($"{Path.GetFileName(file)} does not load: {report.Summary()}");
        foreach (var check in report.Checks.Where(c => c.Status == AvatarCheckStatus.Fail))
            Console.Error.WriteLine($"  {check.Area}/{check.Name}: {check.Detail}");
        return 1;
    }

    private static string PosterSize(AvatarPackage package, string path)
    {
        if (!package.TryGetFile(path, out var bytes))
            return "missing";
        using var codec = SKCodec.Create(new SKMemoryStream(bytes.ToArray()));
        return codec is null ? "not an image" : $"{codec.Info.Width}×{codec.Info.Height}";
    }

    private static byte[] Png(SKImage image)
    {
        using (image)
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            return data.ToArray();
    }

    private static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.##} MB",
    };
}
