"""Packages TalkingHead's mpfb.glb (CC0, MPFB/MakeHuman) as a .spineavatar for the Spine Avatar Lab (#524).

Usage: python package_mpfb.py <mpfb-small.glb> <out-dir> [poster-light.png poster-dark.png]
The GLB is TalkingHead's avatars/mpfb.glb with its textures resized to 512–1024 px (repack_glb.py);
geometry, skin and morph targets are unchanged. It has ARKit blend shapes and the Oculus visemes
(no viseme_sil, which the format allows to be all zero) and no animation clips.
"""
import hashlib, io, json, math, struct, sys, zipfile, zlib

glb_path, out_dir = sys.argv[1], sys.argv[2]
posters = sys.argv[3:5]
glb = open(glb_path, 'rb').read()
length = struct.unpack('<I', glb[12:16])[0]
gltf = json.loads(glb[20:20 + length])

mesh_node = {n['mesh']: i for i, n in enumerate(gltf['nodes']) if 'mesh' in n}
names = {m: gltf['meshes'][m].get('extras', {}).get('targetNames', []) for m in range(len(gltf['meshes']))}
BASE = next(m for m in names if 'viseme_aa' in names[m] and len(names[m]) > 50)
HEAD = next(i for i, n in enumerate(gltf['nodes']) if n.get('name') == 'Head' and 'mesh' not in n)

def shape(name, value):
    """The named blend shape in every mesh that has it (face, brows, lashes, teeth, tongue)."""
    return [{"node": mesh_node[m], "mesh": m, "primitives": [0], "targetIndex": names[m].index(name), "value": value, "expectedName": name}
            for m in names if name in names[m]]

def shapes(**values):
    return [w for name, v in values.items() for w in shape(name, v)]

def qmul(a, b):
    ax, ay, az, aw = a; bx, by, bz, bw = b
    return [aw*bx + ax*bw + ay*bz - az*by, aw*by - ax*bz + ay*bw + az*bx, aw*bz + ax*by - ay*bx + az*bw, aw*bw - ax*bx - ay*by - az*bz]

def axis(x, y, z, angle):
    s = math.sin(angle / 2)
    return [x*s, y*s, z*s, math.cos(angle / 2)]

REST = gltf['nodes'][HEAD].get('rotation', [0, 0, 0, 1])
def head(pitch=0.0, yaw=0.0, roll=0.0):
    delta = qmul(qmul(axis(0, 1, 0, yaw), axis(1, 0, 0, pitch)), axis(0, 0, 1, roll))
    return [{"node": HEAD, "property": "rotation", "value": [round(v, 6) for v in qmul(REST, delta)]}]

visemes = ["sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS", "nn", "RR", "aa", "E", "I", "O", "U"]
poses = {
    "idle": [], "connecting": [], "speaking": [], "reduced_motion": [],
    "listening": head(pitch=-0.06, roll=0.05) + shapes(browInnerUp=0.15),
    "thinking": head(pitch=-0.08, yaw=0.18) + shapes(eyeLookUpLeft=0.35, eyeLookUpRight=0.35, browDownLeft=0.25, mouthPressLeft=0.3),
    "interrupted": shapes(eyeWideLeft=0.5, eyeWideRight=0.5, browInnerUp=0.4),
    "muted": shapes(mouthPressLeft=0.3, mouthPressRight=0.3) + head(pitch=0.06),
    "expr_neutral": [],
    "expr_happy": shapes(mouthSmileLeft=0.7, mouthSmileRight=0.7, cheekSquintLeft=0.4, cheekSquintRight=0.4, eyeSquintLeft=0.2, eyeSquintRight=0.2),
    "expr_curious": shapes(browOuterUpLeft=0.6, browInnerUp=0.3, eyeWideLeft=0.15, eyeWideRight=0.15) + head(roll=0.12),
    "expr_thinking": shapes(eyeLookUpLeft=0.3, eyeLookUpRight=0.3, browDownLeft=0.3, mouthPressLeft=0.3) + head(yaw=0.15),
    "expr_concerned": shapes(browInnerUp=0.7, mouthFrownLeft=0.4, mouthFrownRight=0.4),
    "expr_surprised": shapes(eyeWideLeft=0.8, eyeWideRight=0.8, browInnerUp=0.8, browOuterUpLeft=0.6, browOuterUpRight=0.6, jawOpen=0.25),
    "expr_apologetic": shapes(browInnerUp=0.6, mouthFrownLeft=0.2, mouthFrownRight=0.2, eyeSquintLeft=0.2, eyeSquintRight=0.2) + head(pitch=0.1),
    "expr_confident": shapes(mouthSmileLeft=0.35, mouthSmileRight=0.35, browDownLeft=0.15, browDownRight=0.15, eyeSquintLeft=0.15, eyeSquintRight=0.15),
}
for v in visemes:
    poses["viseme_" + v] = shape("viseme_" + v, 1.0)

mouth = ["mouthSmileLeft", "mouthSmileRight", "mouthFrownLeft", "mouthFrownRight", "mouthPressLeft", "mouthPressRight", "jawOpen"]
bindings = {
    "schemaVersion": "1.0",
    "renderer": "native3d",
    "poses": poses,
    "animations": {},
    "parameters": {},
    "channelMasks": {"expression": ["face", "headTransform"], "speech": ["mouthShape"], "idle": [], "reflex": ["eyes"]},
    "framing": {"cameraPosition": [0, 1.6, 0.95], "lookAt": [0, 1.57, 0], "verticalFov": 28, "safeInset": 0.04, "fit": "contain"},
    "headNode": HEAD,
    "speechMouthMesh": BASE,
    "expressionMouthTargets": [names[BASE].index(n) for n in mouth],
    "blink": [e for n in ("eyeBlinkLeft", "eyeBlinkRight") for e in shape(n, 1)],
    # No idle clips in the model: the scheduler's gaze turns the head a little, which keeps it alive.
    "gaze": {"node": HEAD, "headRotation": 0.6},
}

manifest = {
    "schemaVersion": "1.0", "id": "mpfb", "displayName": "MPFB Human", "assetVersion": "1.0.0",
    "minRuntimeVersion": "1.0.0", "profile": "character",
    "representations": [{
        "id": "native3d-primary", "renderer": "native3d", "format": "glb",
        "model": "models/mpfb.glb", "bindings": "bindings/native3d.json",
        "capabilities": ["expressions", "speechArticulation", "blink", "gaze", "reducedMotion"],
        "platforms": ["android", "ios", "maccatalyst", "windows"],
        "idleOwner": "scheduler", "blinkOwner": "scheduler", "gazeOwner": "scheduler",
    }],
    "states": {s: {"pose": s, "transitionMs": 90 if s == "interrupted" else 220, "inputReactive": s == "listening",
                   "outputReactive": s == "speaking", "gaze": "processing" if s == "thinking" else "engaged"}
               for s in ("idle", "connecting", "listening", "thinking", "speaking", "interrupted", "muted")},
    "expressions": {e: {"pose": "expr_" + e, "channels": ["face", "headTransform"], "transitionMs": 260}
                    for e in ("neutral", "happy", "curious", "thinking", "concerned", "surprised", "apologetic", "confident")},
    "speech": {"mode": "canonicalVisemes", "canonicalProfile": "oculus15-v1",
               "mapping": {str(i): "viseme_" + v for i, v in enumerate(visemes)}, "expressionMouthScale": 0.45, "releaseMs": 80},
    "motions": {"idleVariants": [], "gestures": {}, "seedable": True, "blinkIntervalSeconds": [2.8, 6.5]},
    "themes": {"slots": {}},
    "reducedMotion": {"pose": "reduced_motion", "retainSpeech": True, "transitionMs": 180},
    "posters": {"light": "previews/poster-light.png", "dark": "previews/poster-dark.png"},
    "license": "LICENSE.txt", "provenance": "provenance.json",
}

def png_1x1():
    chunk = lambda t, d: struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d))
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', 1, 1, 8, 6, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(b'\x00\x00\x00\x00\x00')) + chunk(b'IEND', b'')

files = {
    "models/mpfb.glb": glb,
    "bindings/native3d.json": json.dumps(bindings, indent=1).encode(),
    "previews/poster-light.png": open(posters[0], 'rb').read() if posters else png_1x1(),
    "previews/poster-dark.png": open(posters[1], 'rb').read() if posters else png_1x1(),
    "LICENSE.txt": b"mpfb.glb from TalkingHead (https://github.com/met4citizen/TalkingHead), made with Blender and the MPFB extension\n"
                   b"(MakeHuman assets). Licensed CC0 1.0 per the TalkingHead README. Textures resized for this package.\n",
    "provenance.json": json.dumps({
        "model": "met4citizen/TalkingHead main avatars/mpfb.glb, textures resized to 512-1024 px (JPEG/PNG), geometry unchanged",
        "license": "CC0-1.0", "packagedBy": "source/package_mpfb.py (Spine Avatar Lab, #524)",
        "posters": "captured from the lab's SceneKit surface" if posters else "placeholder 1x1, not a render",
    }, indent=1).encode(),
    "source/package_mpfb.py": open(__file__, 'rb').read(),
    "validation/report.json": json.dumps({"nativeMaui": "NotMeasured", "triangles": "83686, over the spec's 35k budget"}, indent=1).encode(),
}
manifest["files"] = [{"path": p, "sha256": hashlib.sha256(b).hexdigest(), "bytes": len(b)} for p, b in sorted(files.items())]
out = io.BytesIO()
with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as z:
    z.writestr("avatar.json", json.dumps(manifest, indent=1))
    for p, b in sorted(files.items()):
        z.writestr(p, b)
open(f"{out_dir}/mpfb.spineavatar", 'wb').write(out.getvalue())
print(f"{out_dir}/mpfb.spineavatar", len(out.getvalue()), "bytes")
