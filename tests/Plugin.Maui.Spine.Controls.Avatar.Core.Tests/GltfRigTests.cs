using System.Numerics;
using Plugin.Maui.Spine.Controls.Avatar.Core.Gltf;

namespace Plugin.Maui.Spine.Controls.Avatar.Core.Tests;

public class GltfRigTests
{
    private static (GltfModel Model, GltfAvatarRig Rig) Load(string avatar)
    {
        var package = AvatarArchiveTests.Load(avatar);
        var representation = package.Manifest.Representations[0];
        var model = GltfModel.Read(package.GetFile(representation.Model), representation.Model);
        return (model, new GltfAvatarRig(model, package.GetBindings(representation)));
    }

    [Theory]
    [InlineData("pebble-bot", 8, 6, 12)]
    [InlineData("robot-expressive", 74, 14, 14)]
    public void ModelReads(string avatar, int nodes, int meshes, int clips)
    {
        var (model, _) = Load(avatar);

        Assert.Equal(nodes, model.Nodes.Length);
        Assert.Equal(meshes, model.Meshes.Length);
        Assert.Equal(clips, model.Clips.Length);
        Assert.All(model.Meshes.SelectMany(m => m.Primitives), p => Assert.Equal(0, p.Indices.Length % 3));
    }

    [Fact]
    public void ClosedVisemeSetsItsMorphTarget()
    {
        var (model, rig) = Load("pebble-bot");
        var frame = new AvatarRenderFrame { ExpressionMouthScale = 0.45f, SpeakingWeight = 1 };
        frame.AddActivity("speaking", 1);
        frame.AddExpression("expr_happy", 1);
        frame.AddSpeech("viseme_PP", 1);

        rig.Apply(frame);

        var mouth = Array.FindIndex(model.Meshes, m => m.Name == "Mouth");
        Assert.Equal(1, rig.Weights[mouth][1], 3);
        // The expression's mouth target is damped while speaking.
        Assert.Equal(0.45f, rig.Weights[mouth][16], 2);
    }

    [Fact]
    public void GestureOverridesTheIdleOnASkeletalRig()
    {
        var (model, rig) = Load("robot-expressive");
        var head = Array.FindIndex(model.Nodes, n => n.Name == "Head" && n.Mesh < 0);
        var frame = new AvatarRenderFrame();
        frame.AddActivity("idle", 1);
        frame.AddClip("Idle", 0.5, 1, AvatarClipLayer.Idle);
        rig.Apply(frame);
        var idle = rig.Rotation[head];

        frame.AddClip("Yes", 0.5, 1, AvatarClipLayer.Gesture);
        rig.Apply(frame);

        Assert.True(Quaternion.Dot(idle, rig.Rotation[head]) < 0.9999f, "the nod should move the head");
        Assert.All(rig.Translation, t => Assert.True(float.IsFinite(t.X) && float.IsFinite(t.Y) && float.IsFinite(t.Z)));
    }

    [Fact]
    public void SparseMorphTargetsMoveTheJaw()
    {
        var (model, rig) = Load("mpfb");
        var frame = new AvatarRenderFrame { SpeakingWeight = 1 };
        frame.AddActivity("speaking", 1);
        frame.AddSpeech("viseme_aa", 1);
        rig.Apply(frame);

        var head = Array.FindIndex(model.Meshes, m => m.TargetNames.Contains("viseme_aa"));
        var target = Array.IndexOf(model.Meshes[head].TargetNames, "viseme_aa");
        Assert.Equal(1, rig.Weights[head][target], 3);
        Assert.Contains(model.Meshes[head].Primitives.SelectMany(p => p.Targets[target].Positions), d => MathF.Abs(d) > 1e-3f);
    }

    [Fact]
    public void ApplyDoesNotAllocate()
    {
        var (_, rig) = Load("pebble-bot");
        var frame = new AvatarRenderFrame { SpeakingWeight = 1, OutputLevel = 0.5f, OutputReactiveWeight = 1, Blink = 0.3f, Squash = 0.02f };
        frame.AddActivity("speaking", 1);
        frame.AddExpression("expr_happy", 0.8f);
        frame.AddSpeech("viseme_aa", 1);
        frame.AddClip("idle_a", 1.2, 1, AvatarClipLayer.Idle);
        rig.Apply(frame);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 200; i++)
            rig.Apply(frame);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void LinearRotationSamplesStayNormalized()
    {
        var (model, _) = Load("robot-expressive");
        var values = new float[4];
        foreach (var channel in model.Clips.SelectMany(c => c.Channels).Where(c => c.Path == GltfPath.Rotation).Take(20))
        {
            channel.Sample(channel.Times[^1] * 0.37f, values);
            Assert.Equal(1, MathF.Sqrt(values.Sum(v => v * v)), 3);
        }
    }
}
