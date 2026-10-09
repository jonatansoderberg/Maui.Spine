#if IOS || MACCATALYST
using System.Diagnostics;
using System.Numerics;
using CoreGraphics;
using Foundation;
using Plugin.Maui.Spine.Controls.Avatar.Core;
using Plugin.Maui.Spine.Controls.Avatar.Core.Gltf;
using SceneKit;
using UIKit;

namespace Plugin.Maui.Spine.Controls.Avatar.ThreeD;

/// <summary>
/// A GLB avatar drawn natively by SceneKit (Metal), no web view: geometry and PBR materials built from
/// <see cref="GltfModel"/>, morph targets on <see cref="SCNMorpher"/>, skins on <see cref="SCNSkinner"/>,
/// and every frame's node transforms and weights from the shared <see cref="GltfAvatarRig"/>.
/// </summary>
internal sealed class SceneKitAvatarSurface : IAvatarSurface
{
    private readonly SCNView _view;
    private readonly SCNScene _scene = SCNScene.Create();
    private readonly SCNNode _container = SCNNode.Create();
    private readonly SCNNode _cameraNode = SCNNode.Create();
    private readonly SCNNode[] _nodes;
    private readonly List<(int Mesh, SCNMorpher Morpher)> _morphers = [];
    private readonly Dictionary<string, SCNMaterial[]> _materials = new(StringComparer.Ordinal);
    private readonly GltfAvatarRig _rig;
    private readonly AvatarPackage _package;
    private readonly float _verticalFov;
    private readonly List<SCNNode> _studio = [];
    private bool? _dark;
    private string? _accent;

    public SceneKitAvatarSurface(AvatarPackage package, AvatarRepresentation representation)
    {
        _package = package;
        var model = GltfModel.Read(package.GetFile(representation.Model), representation.Model);
        var bindings = package.GetBindings(representation);
        _rig = new GltfAvatarRig(model, bindings);

        _view = new SCNView(CGRect.Empty, (NSDictionary?)null)
        {
            BackgroundColor = UIColor.Clear,
            Scene = _scene,
            RendersContinuously = true,
            AntialiasingMode = SCNAntialiasingMode.Multisampling4X,
            UserInteractionEnabled = false,
            PreferredFramesPerSecond = 60,
        };
        _scene.Background.Contents = UIColor.Clear;
        _view.WeakSceneRendererDelegate = _timer;
        _scene.RootNode.AddChildNode(_container);

        _nodes = [.. model.Nodes.Select(n => SCNNode.Create())];
        for (var i = 0; i < model.Nodes.Length; i++)
        {
            _nodes[i].Name = model.Nodes[i].Name;
            foreach (var child in model.Nodes[i].Children)
                _nodes[i].AddChildNode(_nodes[child]);
        }
        foreach (var root in model.SceneRoots)
            _container.AddChildNode(_nodes[root]);

        var images = model.Images.Select(i => UIImage.LoadFromData(NSData.FromArray(i.Bytes.ToArray()))).ToArray();
        var materials = model.Materials.Select(m => Material(m, images)).ToArray();
        for (var i = 0; i < model.Nodes.Length; i++)
        {
            var node = model.Nodes[i];
            if (node.Mesh >= 0)
                AttachMesh(model, i, materials);
        }
        BindThemes(materials);

        var framing = bindings.Framing?.Extra;
        _verticalFov = framing?.TryGetValue("verticalFov", out var fov) == true ? fov.GetSingle() : 35;
        var position = framing?.TryGetValue("cameraPosition", out var cp) == true ? new SCNVector3(cp[0].GetSingle(), cp[1].GetSingle(), cp[2].GetSingle()) : new SCNVector3(0, 0, 3.5f);
        var lookAt = framing?.TryGetValue("lookAt", out var la) == true ? new SCNVector3(la[0].GetSingle(), la[1].GetSingle(), la[2].GetSingle()) : new SCNVector3(0, 0, 0);
        _cameraNode.Camera = new SCNCamera
        {
            FieldOfView = _verticalFov,
            ProjectionDirection = SCNCameraProjectionDirection.Vertical,
            ZNear = 0.01,
            ZFar = 100,
            WantsHdr = true,
            WantsExposureAdaptation = false,
            BloomIntensity = 0.45f,
            BloomThreshold = 0.85f,
            BloomBlurRadius = 6,
        };
        _cameraNode.Position = position;
        _scene.RootNode.AddChildNode(_cameraNode);
        _cameraNode.Look(lookAt);
        _view.PointOfView = _cameraNode;

        AddLights(model);
        View = new NativeViewHost(_view);
        _view.LayoutSubviews();
    }

    public View View { get; }

    public string RendererName => "native3d (SceneKit)";

    public AvatarFrameStats Stats { get; } = new();

    public string? Detail => _timer.Describe();

    private readonly RenderTimer _timer = new();

    /// <summary>Times SceneKit's render thread from will-render to did-render: CPU work to encode a frame, not GPU time.</summary>
    private sealed class RenderTimer : NSObject, ISCNSceneRendererDelegate
    {
        private long _start, _frames, _windowStart = Stopwatch.GetTimestamp();
        private double _total, _fps, _ms;

        [Export("renderer:willRenderScene:atTime:")]
        public void WillRenderScene(ISCNSceneRenderer renderer, SCNScene scene, double timeInSeconds) => _start = Stopwatch.GetTimestamp();

        [Export("renderer:didRenderScene:atTime:")]
        public void DidRenderScene(ISCNSceneRenderer renderer, SCNScene scene, double timeInSeconds)
        {
            _total += Stopwatch.GetElapsedTime(_start).TotalMilliseconds;
            _frames++;
            var window = Stopwatch.GetElapsedTime(_windowStart).TotalSeconds;
            if (window >= 1)
            {
                _fps = _frames / window;
                _ms = _total / Math.Max(1, _frames);
                _frames = 0;
                _total = 0;
                _windowStart = Stopwatch.GetTimestamp();
            }
        }

        public string Describe() => $"SceneKit {_fps:0.0} fps, render thread {_ms:0.00} ms per frame";
    }

    public void Render(AvatarRenderFrame frame, bool dark, Color? accent)
    {
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();

        _rig.Apply(frame);
        for (var i = 0; i < _nodes.Length; i++)
        {
            var t = _rig.Translation[i];
            var r = _rig.Rotation[i];
            var s = _rig.Scale[i];
            _nodes[i].Position = new SCNVector3(t.X, t.Y, t.Z);
            _nodes[i].Orientation = new SCNQuaternion(r.X, r.Y, r.Z, r.W);
            _nodes[i].Scale = new SCNVector3(s.X, s.Y, s.Z);
        }
        foreach (var (mesh, morpher) in _morphers)
        {
            var weights = _rig.Weights[mesh];
            for (var k = 0; k < weights.Length; k++)
                morpher.SetWeight(weights[k], (nuint)k);
        }
        var rootScale = _rig.RootScale;
        _container.Scale = new SCNVector3(rootScale.X, rootScale.Y, rootScale.Z);
        _container.Position = new SCNVector3(_rig.RootOffset.X, _rig.RootOffset.Y, _rig.RootOffset.Z);

        var accentHex = accent?.ToArgbHex();
        if (_dark != dark || _accent != accentHex)
            ApplyTheme(dark, accentHex);

        FitCamera();
        Stats.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocated);
    }

    public void Dispose()
    {
        _view.Scene = null;
        _view.RemoveFromSuperview();
        _view.Dispose();
    }

    private void AttachMesh(GltfModel model, int nodeIndex, SCNMaterial[] materials)
    {
        var node = model.Nodes[nodeIndex];
        var mesh = model.Meshes[node.Mesh];
        foreach (var primitive in mesh.Primitives)
        {
            var element = SCNGeometryElement.FromData(
                NSData.FromArray(ToBytes(primitive.Indices)), SCNGeometryPrimitiveType.Triangles, primitive.Indices.Length / 3, sizeof(int));
            var sources = new List<SCNGeometrySource> { SCNGeometrySource.FromVertices(Vectors(primitive.Positions)) };
            sources.Add(SCNGeometrySource.FromNormals(Vectors(primitive.Normals ?? FlatNormals(primitive))));
            if (primitive.TexCoords is { } uv)
                sources.Add(TexCoords(uv));
            var geometry = SCNGeometry.Create([.. sources], [element]);
            geometry.Materials = [primitive.Material >= 0 ? materials[primitive.Material] : Material(new GltfMaterial("", Vector4.One, 0, 0.6f, Vector3.Zero, false, false), [])];

            var target = SCNNode.FromGeometry(geometry);
            if (primitive.Targets.Length > 0)
            {
                var morpher = new SCNMorpher
                {
                    CalculationMode = SCNMorpherCalculationMode.Additive,
                    Targets = [.. primitive.Targets.Select(t => SCNGeometry.Create(
                        [SCNGeometrySource.FromVertices(Vectors(t.Positions)), SCNGeometrySource.FromNormals(Vectors(t.Normals ?? new float[t.Positions.Length]))],
                        [element]))],
                };
                target.Morpher = morpher;
                _morphers.Add((node.Mesh, morpher));
            }

            if (node.Skin >= 0 && primitive.Joints is { } joints && primitive.Weights is { } weights)
            {
                var skin = model.Skins[node.Skin];
                var bones = skin.Joints.Select(j => _nodes[j]).ToArray();
                var inverse = skin.InverseBindMatrices.Select(ToSceneKit).ToArray();
                var indexData = new byte[joints.Length * 2];
                for (var i = 0; i < joints.Length; i++)
                    BitConverter.TryWriteBytes(indexData.AsSpan(i * 2), (ushort)joints[i]);
                var boneIndices = SCNGeometrySource.FromData(NSData.FromArray(indexData), SCNGeometrySourceSemantics.BoneIndices,
                    primitive.VertexCount, false, 4, 2, 0, 8);
                var boneWeights = SCNGeometrySource.FromData(NSData.FromArray(ToBytes(weights)), SCNGeometrySourceSemantics.BoneWeights,
                    primitive.VertexCount, true, 4, 4, 0, 16);
                target.Skinner = SCNSkinner.Create(geometry, bones, inverse, boneWeights, boneIndices);
                // A skinned mesh is placed by its joints, not by its node (glTF), so it hangs off the container.
                _container.AddChildNode(target);
            }
            else
                _nodes[nodeIndex].AddChildNode(target);
        }
    }

    private static SCNGeometrySource TexCoords(float[] uv) =>
        SCNGeometrySource.FromData(NSData.FromArray(ToBytes(uv)), SCNGeometrySourceSemantics.Texcoord, uv.Length / 2, true, 2, sizeof(float), 0, 2 * sizeof(float));

    // Matrices go through a node's transform so the SCNMatrix4 layout is SceneKit's own, whatever the binding exposes.
    private static SCNMatrix4 ToSceneKit(Matrix4x4 m)
    {
        Matrix4x4.Decompose(m, out var scale, out var rotation, out var translation);
        using var node = SCNNode.Create();
        node.Position = new SCNVector3(translation.X, translation.Y, translation.Z);
        node.Orientation = new SCNQuaternion(rotation.X, rotation.Y, rotation.Z, rotation.W);
        node.Scale = new SCNVector3(scale.X, scale.Y, scale.Z);
        return node.Transform;
    }

    private static SCNMaterial Material(GltfMaterial m, UIImage?[] images)
    {
        var material = SCNMaterial.Create();
        material.LightingModelName = SCNLightingModel.PhysicallyBased;
        UIImage? Image(int index) => index >= 0 && index < images.Length ? images[index] : null;
        if (Image(m.BaseColorTexture) is { } color)
        {
            material.Diffuse.Contents = color;
            // glTF's default sampler repeats; SceneKit clamps unless told.
            material.Diffuse.WrapS = material.Diffuse.WrapT = SCNWrapMode.Repeat;
            // glTF multiplies the texture by the factor; SceneKit's multiply slot does the same.
            if (m.BaseColor != Vector4.One)
                material.Multiply.Contents = Srgb(m.BaseColor.X, m.BaseColor.Y, m.BaseColor.Z, m.BaseColor.W);
        }
        else
            material.Diffuse.Contents = Srgb(m.BaseColor.X, m.BaseColor.Y, m.BaseColor.Z, m.BaseColor.W);
        if (Image(m.NormalTexture) is { } normal)
        {
            material.Normal.Contents = normal;
            material.Normal.WrapS = material.Normal.WrapT = SCNWrapMode.Repeat;
        }
        if (Image(m.EmissiveTexture) is { } emissive)
            material.Emission.Contents = emissive;
        if (m.Mask)
        {
            // SceneKit has no alpha test; a fragment modifier discards what glTF's MASK mode would.
            var cutoff = m.AlphaCutoff.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            material.ShaderModifiers = new SCNShaderModifiers { EntryPointFragment = $"if (_output.color.a < {cutoff}) discard_fragment();" };
        }
        else if (m.Blend)
            material.BlendMode = SCNBlendMode.Alpha;
        material.Metalness.Contents = NSNumber.FromFloat(m.Metallic);
        material.Roughness.Contents = NSNumber.FromFloat(m.Roughness);
        if (m.Emissive != Vector3.Zero)
            material.Emission.Contents = Srgb(m.Emissive.X, m.Emissive.Y, m.Emissive.Z, 1);
        material.DoubleSided = m.DoubleSided;
        material.Name = m.Name;
        return material;
    }

    private void BindThemes(SCNMaterial[] materials)
    {
        foreach (var (name, slot) in _package.Manifest.Themes.Slots)
        {
            var bound = slot.Bindings
                .Where(b => b.StartsWith("material:", StringComparison.Ordinal))
                .Select(b => int.Parse(b[9..], System.Globalization.CultureInfo.InvariantCulture))
                .Where(i => i < materials.Length)
                .Select(i => materials[i]).ToArray();
            _materials[name] = bound;
        }
    }

    private void ApplyTheme(bool dark, string? accent)
    {
        foreach (var (name, slot) in _package.Manifest.Themes.Slots)
        {
            var hex = name == "accent" && accent is not null ? accent : dark ? slot.Dark : slot.Light;
            var color = Color.FromArgb(hex);
            foreach (var material in _materials[name])
            {
                // A textured material keeps its texture and takes the colour as its factor.
                var property = material.Diffuse.Contents is UIImage ? material.Multiply : material.Diffuse;
                property.Contents = UIColor.FromRGBA(color.Red, color.Green, color.Blue, color.Alpha);
            }
        }
        _dark = dark;
        _accent = accent;
    }

    private void AddLights(GltfModel model)
    {
        SCNNode Light(NSString type, UIColor color, float intensity, SCNVector3? from)
        {
            var node = SCNNode.Create();
            node.Light = new SCNLight { LightType = type, Color = color, Intensity = intensity };
            if (from is { } p)
            {
                node.Position = p;
                node.Look(new SCNVector3(0, (_rig.BoundsMin.Y + _rig.BoundsMax.Y) / 2, 0));
            }
            _scene.RootNode.AddChildNode(node);
            return node;
        }

        Light(SCNLightType.Directional, UIColor.FromRGB(255, 244, 232), 1300, new SCNVector3(-2.5f, 3, 4));
        Light(SCNLightType.Directional, UIColor.FromRGB(159, 232, 255), 1500, new SCNVector3(2.5f, 2.5f, -3.5f));
        Light(SCNLightType.Ambient, UIColor.FromRGB(200, 210, 225), 180, null);

        _scene.LightingEnvironment.Contents = StudioEnvironment();
        _scene.LightingEnvironment.Intensity = 0.9f;

        var size = _rig.BoundsMax - _rig.BoundsMin;
        var plane = SCNPlane.Create(size.X * 1.3f, size.Z * 1.3f + size.X * 0.25f);
        var material = SCNMaterial.Create();
        material.LightingModelName = SCNLightingModel.Constant;
        material.Diffuse.Contents = ShadowImage();
        material.WritesToDepthBuffer = false;
        material.BlendMode = SCNBlendMode.Alpha;
        plane.Materials = [material];
        var shadow = SCNNode.FromGeometry(plane);
        shadow.EulerAngles = new SCNVector3(-MathF.PI / 2, 0, 0);
        shadow.Position = new SCNVector3((_rig.BoundsMin.X + _rig.BoundsMax.X) / 2, _rig.BoundsMin.Y + 0.002f, (_rig.BoundsMin.Z + _rig.BoundsMax.Z) / 2);
        shadow.RenderingOrder = -1;
        _container.AddChildNode(shadow);
    }

    // A tall view keeps the model's width by widening the vertical field of view, as the web surface does.
    private void FitCamera()
    {
        var bounds = _view.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;
        var aspect = bounds.Width / bounds.Height;
        var fov = aspect < 1 ? 2 * Math.Atan(Math.Tan(_verticalFov * Math.PI / 360) / aspect) * 180 / Math.PI : _verticalFov;
        if (Math.Abs(_cameraNode.Camera!.FieldOfView - fov) > 0.01)
            _cameraNode.Camera.FieldOfView = (nfloat)fov;
    }

    // A small equirectangular studio: a bright ceiling, a softbox band and a dark floor, for reflections.
    private static UIImage StudioEnvironment()
    {
        var renderer = new UIGraphicsImageRenderer(new CGSize(256, 128));
        return renderer.CreateImage(context =>
        {
            var g = context.CGContext;
            using var space = CGColorSpace.CreateSrgb()!;
            using var gradient = new CGGradient(space,
                [new CGColor(0.95f, 0.97f, 1f), new CGColor(0.62f, 0.68f, 0.76f), new CGColor(0.16f, 0.18f, 0.22f)],
                [0f, 0.45f, 1f]);
            g.DrawLinearGradient(gradient, new CGPoint(0, 0), new CGPoint(0, 128), 0);
            g.SetFillColor(new CGColor(1, 1, 1, 0.9f));
            g.FillRect(new CGRect(40, 30, 50, 22));
            g.FillRect(new CGRect(170, 34, 40, 18));
        });
    }

    private static UIImage ShadowImage()
    {
        var renderer = new UIGraphicsImageRenderer(new CGSize(128, 128), new UIGraphicsImageRendererFormat { Opaque = false });
        return renderer.CreateImage(context =>
        {
            using var space = CGColorSpace.CreateSrgb()!;
            using var gradient = new CGGradient(space, [new CGColor(0, 0, 0, 0.35f), new CGColor(0, 0, 0, 0)], [0f, 1f]);
            context.CGContext.DrawRadialGradient(gradient, new CGPoint(64, 64), 0, new CGPoint(64, 64), 64, 0);
        });
    }

    private static UIColor Srgb(float r, float g, float b, float a)
    {
        static float Encode(float linear) => linear <= 0.0031308f ? 12.92f * linear : 1.055f * MathF.Pow(linear, 1 / 2.4f) - 0.055f;
        return UIColor.FromRGBA(Encode(r), Encode(g), Encode(b), a);
    }

    private static SCNVector3[] Vectors(float[] xyz)
    {
        var result = new SCNVector3[xyz.Length / 3];
        for (var i = 0; i < result.Length; i++)
            result[i] = new SCNVector3(xyz[i * 3], xyz[i * 3 + 1], xyz[i * 3 + 2]);
        return result;
    }

    private static float[] FlatNormals(GltfPrimitive p)
    {
        var normals = new float[p.Positions.Length];
        for (var i = 0; i < p.Indices.Length; i += 3)
        {
            var a = p.Indices[i] * 3; var b = p.Indices[i + 1] * 3; var c = p.Indices[i + 2] * 3;
            var n = Vector3.Normalize(Vector3.Cross(
                new Vector3(p.Positions[b] - p.Positions[a], p.Positions[b + 1] - p.Positions[a + 1], p.Positions[b + 2] - p.Positions[a + 2]),
                new Vector3(p.Positions[c] - p.Positions[a], p.Positions[c + 1] - p.Positions[a + 1], p.Positions[c + 2] - p.Positions[a + 2])));
            foreach (var v in new[] { a, b, c })
            {
                normals[v] += n.X; normals[v + 1] += n.Y; normals[v + 2] += n.Z;
            }
        }
        return normals;
    }

    private static byte[] ToBytes(int[] values)
    {
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static byte[] ToBytes(float[] values)
    {
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
#endif
