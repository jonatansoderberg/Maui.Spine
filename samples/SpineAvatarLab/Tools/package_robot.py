"""Packages three.js RobotExpressive.glb (CC0) as a .spineavatar for the Spine Avatar Lab (#524).

Usage: python package_robot.py <RobotExpressive.glb> <out-dir> [poster-light.png poster-dark.png]
The model bytes are unchanged; this script only writes the manifest, bindings and metadata.
"""
import hashlib, io, json, math, struct, sys, zipfile, zlib

glb_path, out_dir = sys.argv[1], sys.argv[2]
posters = sys.argv[3:5]
glb = open(glb_path, 'rb').read()
length = struct.unpack('<I', glb[12:16])[0]
gltf = json.loads(glb[20:20 + length])

HEAD_MESH_NODE, HEAD_MESH, HEAD_BONE = 13, 2, 12
assert gltf['nodes'][HEAD_MESH_NODE]['mesh'] == HEAD_MESH and gltf['nodes'][HEAD_BONE]['name'] == 'Head'
names = gltf['meshes'][HEAD_MESH]['extras']['targetNames']
ANGRY, SURPRISED, SAD = (names.index(n) for n in ('Angry', 'Surprised', 'Sad'))
PRIMS = list(range(len(gltf['meshes'][HEAD_MESH]['primitives'])))

def morph(target, value):
    return {"node": HEAD_MESH_NODE, "mesh": HEAD_MESH, "primitives": PRIMS, "targetIndex": target, "value": value,
            "expectedName": names[target]}

def qmul(a, b):
    ax, ay, az, aw = a; bx, by, bz, bw = b
    return [aw*bx + ax*bw + ay*bz - az*by, aw*by - ax*bz + ay*bw + az*bx, aw*bz + ax*by - ay*bx + az*bw, aw*bw - ax*bx - ay*by - az*bz]

def axis(x, y, z, angle):
    s = math.sin(angle / 2)
    return [x*s, y*s, z*s, math.cos(angle / 2)]

REST = gltf['nodes'][HEAD_BONE]['rotation']
def head(pitch=0.0, yaw=0.0, roll=0.0):
    # Bone-local axes (Blender convention): X nods, Y turns, Z tilts sideways. Poses are absolute targets,
    # so the delta is applied to the rest rotation.
    delta = qmul(qmul(axis(0, 1, 0, yaw), axis(1, 0, 0, pitch)), axis(0, 0, 1, roll))
    return {"node": HEAD_BONE, "property": "rotation", "value": [round(v, 6) for v in qmul(REST, delta)]}

poses = {
    "idle": [], "connecting": [], "speaking": [],
    "listening": [head(pitch=-0.10, roll=0.06)],
    "thinking": [head(pitch=0.12, yaw=0.25, roll=-0.10), morph(SAD, 0.25)],
    "interrupted": [morph(SURPRISED, 0.6)],
    "muted": [morph(SAD, 0.35), head(pitch=0.15)],
    "reduced_motion": [],
    "expr_neutral": [],
    "expr_happy": [head(pitch=-0.08, roll=0.10)],
    "expr_curious": [morph(SURPRISED, 0.35), head(roll=0.22)],
    "expr_thinking": [morph(SAD, 0.3), head(pitch=0.10, yaw=0.3)],
    "expr_concerned": [morph(SAD, 0.75), head(pitch=0.08)],
    "expr_surprised": [morph(SURPRISED, 1.0), head(pitch=-0.12)],
    "expr_apologetic": [morph(SAD, 1.0), head(pitch=0.22, roll=-0.08)],
    "expr_confident": [morph(ANGRY, 0.35), head(pitch=-0.12)],
}

clips = {a['name'] for a in gltf['animations']}
animations = {n: n for n in ("Idle", "Yes", "No", "Wave", "ThumbsUp", "Jump", "Dance")}
assert set(animations.values()) <= clips

bindings = {
    "schemaVersion": "1.0",
    "renderer": "native3d",
    "poses": poses,
    "animations": animations,
    "parameters": {
        # No mouth to shape: the open-mouthed Surprised morph follows the output level (AudioReactive).
        "outputLevel": [{"node": HEAD_MESH_NODE, "mesh": HEAD_MESH, "primitives": PRIMS, "targetIndex": SURPRISED, "min": 0, "max": 0.55}],
    },
    "channelMasks": {"expression": ["face", "headTransform"], "speech": [], "idle": ["body"], "reflex": ["headTransform"]},
    "framing": {"cameraPosition": [0, 2.9, 10.5], "lookAt": [0, 2.45, 0], "verticalFov": 32, "safeInset": 0.04, "fit": "contain"},
    "headNode": HEAD_BONE,
    # Skeletal clips hold the whole pose, so they replace each other (a gesture fades the idle out)
    # instead of adding to a rest pose as the node-rig avatars' clips do.
    "clipBlend": "override",
}

manifest = {
    "schemaVersion": "1.0", "id": "robot-expressive", "displayName": "Robot Expressive", "assetVersion": "1.0.0",
    "minRuntimeVersion": "1.0.0", "profile": "ambient",
    "representations": [{
        "id": "native3d-primary", "renderer": "native3d", "format": "glb",
        "model": "models/robot-expressive.glb", "bindings": "bindings/native3d.json",
        "capabilities": ["ambient", "expressions", "audioReactive", "gestures", "reducedMotion", "themeSlots"],
        "platforms": ["android", "ios", "maccatalyst", "windows"],
        "idleOwner": "scheduler", "blinkOwner": "none", "gazeOwner": "none",
    }],
    "states": {s: {"pose": s, "transitionMs": 90 if s == "interrupted" else 220, "inputReactive": s == "listening",
                   "outputReactive": s == "speaking", "gaze": "none"}
               for s in ("idle", "connecting", "listening", "thinking", "speaking", "interrupted", "muted")},
    "expressions": {e: {"pose": "expr_" + e, "channels": ["face", "headTransform"], "transitionMs": 260}
                    for e in ("neutral", "happy", "curious", "thinking", "concerned", "surprised", "apologetic", "confident")},
    "speech": {"mode": "audioReactive", "mapping": {}, "expressionMouthScale": 0.45, "releaseMs": 90},
    "motions": {"idleVariants": ["Idle"],
                "gestures": {"nod": "Yes", "shake": "No", "lean": "Wave", "thumbsUp": "ThumbsUp", "jump": "Jump", "dance": "Dance"},
                "seedable": True},
    "themes": {"slots": {"body": {"light": "#C99437", "dark": "#D9A441", "bindings": ["material:1"]}}},
    "reducedMotion": {"pose": "reduced_motion", "retainSpeech": True, "transitionMs": 180},
    "posters": {"light": "previews/poster-light.png", "dark": "previews/poster-dark.png"},
    "license": "LICENSE.txt", "provenance": "provenance.json",
}

def png_1x1():
    raw = b'\x00\x00\x00\x00\x00'
    chunk = lambda t, d: struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d))
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', 1, 1, 8, 6, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(raw)) + chunk(b'IEND', b'')

files = {
    "models/robot-expressive.glb": glb,
    "bindings/native3d.json": json.dumps(bindings, indent=1).encode(),
    "previews/poster-light.png": open(posters[0], 'rb').read() if posters else png_1x1(),
    "previews/poster-dark.png": open(posters[1], 'rb').read() if posters else png_1x1(),
    "LICENSE.txt": b"RobotExpressive by Tomas Laulhe (Quaternius), CC0 1.0. Facial morph targets added by Don McCurdy.\n"
                   b"Source: https://github.com/mrdoob/three.js/tree/r180/examples/models/gltf/RobotExpressive\n"
                   b"The creator asks users to consider supporting https://www.patreon.com/quaternius\n",
    "provenance.json": json.dumps({
        "model": "three.js r180 examples/models/gltf/RobotExpressive/RobotExpressive.glb, unmodified",
        "license": "CC0-1.0", "packagedBy": "source/package_robot.py (Spine Avatar Lab, #524)",
        "posters": "captured from the lab's three.js surface" if posters else "placeholder 1x1, not a render",
    }, indent=1).encode(),
    "source/authoring-notes.md": b"Bindings are authored in package_robot.py. Head poses are deltas on the Head bone's rest rotation in bone-local axes. "
                                 b"No visemes or blink exist in the model: speech is AudioReactive through the Surprised morph. "
                                 b"Extras outside the authoring profile: headNode, clipBlend=override, gestures thumbsUp/jump/dance.\n",
    "source/package_robot.py": open(__file__, 'rb').read(),
    "validation/report.json": json.dumps({"glTFValidator": "NotMeasured", "nativeMaui": "NotMeasured", "visemes": "NotApplicable (none in model)"}, indent=1).encode(),
}
manifest["files"] = [{"path": p, "sha256": hashlib.sha256(b).hexdigest(), "bytes": len(b)} for p, b in sorted(files.items())]

out = io.BytesIO()
with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as z:
    z.writestr("avatar.json", json.dumps(manifest, indent=1))
    for p, b in sorted(files.items()):
        z.writestr(p, b)
open(f"{out_dir}/robot-expressive.spineavatar", 'wb').write(out.getvalue())
print(f"{out_dir}/robot-expressive.spineavatar", len(out.getvalue()), "bytes")
