using Plugin.Maui.Spine.Controls.Avatar.Core;
using SkiaSharp;

namespace Plugin.Maui.Spine.Controls.Avatar.Skia;

public enum AvatarSheetKind { States, Expressions, Visemes, Combinations, Sizes }

/// <summary>
/// Review sheets rendered from the real model, as the authoring profile asks for: every state,
/// expression and viseme, expressions crossed with speech, and the small sizes. Static poses, no
/// clips, so a sheet is the same on every machine.
/// </summary>
public static class AvatarSheet
{
    private static readonly int[] CombinationVisemes = [0, 1, 10, 13];
    private static readonly int[] Sizes = [64, 128, 320];

    public static byte[] RenderPng(AvatarPackage package, AvatarRepresentation representation, AvatarSheetKind kind, bool dark, int cell = 160)
    {
        var model = Spine2dModel.Compile(package, representation);
        using var renderer = new Spine2dRenderer(model);
        var manifest = package.Manifest;
        var frame = new AvatarRenderFrame();
        var cells = Cells(manifest, kind).ToList();

        var columns = kind switch { AvatarSheetKind.Combinations => CombinationVisemes.Length, AvatarSheetKind.Sizes => Sizes.Length, _ => Math.Min(cells.Count, 8) };
        var rows = (cells.Count + columns - 1) / columns;
        const int label = 22;
        var width = kind == AvatarSheetKind.Sizes ? Sizes.Sum() + 16 * Sizes.Length : columns * cell;
        var height = kind == AvatarSheetKind.Sizes ? Sizes.Max() + label : rows * (cell + label);

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(dark ? new SKColor(0x10, 0x1A, 0x26) : new SKColor(0xF7, 0xFA, 0xFC));
        using var text = new SKPaint { IsAntialias = true, Color = dark ? new SKColor(0xC8, 0xD8, 0xE0) : new SKColor(0x40, 0x58, 0x68) };
        using var font = new SKFont(SKTypeface.Default, 12);

        var x = 0f;
        for (var i = 0; i < cells.Count; i++)
        {
            var (name, setup) = cells[i];
            frame.Clear();
            frame.ExpressionMouthScale = (float)manifest.Speech.ExpressionMouthScale;
            setup(frame);
            renderer.Evaluate(frame);

            SKRect rect;
            if (kind == AvatarSheetKind.Sizes)
            {
                var size = Sizes[i];
                rect = SKRect.Create(x + 8, 0, size, size);
                x += size + 16;
            }
            else
                rect = SKRect.Create(i % columns * cell, i / columns * (cell + label), cell, cell);

            renderer.Draw(canvas, rect, dark);
            canvas.DrawText(name, rect.MidX, rect.Bottom + 15, SKTextAlign.Center, font, text);
        }

        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    private static IEnumerable<(string Name, Action<AvatarRenderFrame> Setup)> Cells(AvatarManifest manifest, AvatarSheetKind kind)
    {
        string StatePose(string state) => manifest.States.TryGetValue(state, out var s) ? s.Pose ?? state : state;
        string? ExpressionPose(string expression) => manifest.Expressions.TryGetValue(expression, out var e) ? e.Pose : null;
        string? VisemePose(int id) => manifest.Speech.Mapping.GetValueOrDefault(id.ToString(System.Globalization.CultureInfo.InvariantCulture));

        void Neutral(AvatarRenderFrame f)
        {
            if (ExpressionPose("neutral") is { } neutral)
                f.AddExpression(neutral, 0.65f);
        }

        switch (kind)
        {
            case AvatarSheetKind.States:
                foreach (var state in AvatarVocabulary.States)
                    yield return (state, f =>
                    {
                        f.AddActivity(StatePose(state), 1);
                        Neutral(f);
                        f.SpeakingWeight = state == "speaking" ? 1 : 0;
                        if (state == "muted") f.MicMutedWeight = 1;
                        // Reactive states are shown at a level of 0.7, as the bundle's own sheets are.
                        if (manifest.States.TryGetValue(state, out var profile))
                        {
                            f.InputReactiveWeight = profile.InputReactive ? 1 : 0;
                            f.OutputReactiveWeight = profile.OutputReactive ? 1 : 0;
                            f.InputLevel = f.OutputLevel = 0.7f;
                        }
                    });
                break;

            case AvatarSheetKind.Expressions:
                foreach (var expression in AvatarVocabulary.Expressions)
                    yield return (expression, f =>
                    {
                        f.AddActivity(StatePose("idle"), 1);
                        if (ExpressionPose(expression) is { } pose) f.AddExpression(pose, 1);
                    });
                break;

            case AvatarSheetKind.Visemes:
                for (var id = 0; id < 15; id++)
                {
                    var viseme = id;
                    yield return ($"{id} {AvatarVocabulary.Visemes[id]}", f =>
                    {
                        f.AddActivity(StatePose("speaking"), 1);
                        Neutral(f);
                        f.SpeakingWeight = 1;
                        if (VisemePose(viseme) is { } pose) f.AddSpeech(pose, 1);
                    });
                }
                break;

            case AvatarSheetKind.Combinations:
                foreach (var expression in AvatarVocabulary.Expressions)
                {
                    foreach (var id in CombinationVisemes)
                    {
                        var viseme = id;
                        yield return ($"{expression} + {AvatarVocabulary.Visemes[id]}", f =>
                        {
                            f.AddActivity(StatePose("speaking"), 1);
                            if (ExpressionPose(expression) is { } pose) f.AddExpression(pose, 1);
                            f.SpeakingWeight = 1;
                            if (VisemePose(viseme) is { } speech) f.AddSpeech(speech, 1);
                        });
                    }
                }
                break;

            case AvatarSheetKind.Sizes:
                foreach (var size in Sizes)
                    yield return ($"{size} px", f =>
                    {
                        f.AddActivity(StatePose("idle"), 1);
                        Neutral(f);
                    });
                break;
        }
    }
}
