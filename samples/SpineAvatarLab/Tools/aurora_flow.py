"""Packages Aurora Flow, an sksl avatar (one shader, ribbons of light), for the Spine Avatar Lab (#524).

Usage: python aurora_flow.py <out-dir> [poster-light.png poster-dark.png]
The shader is aurora_flow.sksl beside this script; its uniforms follow AvatarShaderContract.
States, expressions and visemes have empty poses: the runtime turns their weights into uniforms.
"""
import hashlib, io, json, os, struct, sys, zipfile, zlib

out_dir = sys.argv[1]
posters = sys.argv[2:4]
here = os.path.dirname(os.path.abspath(__file__))
shader = open(os.path.join(here, "aurora_flow.sksl"), 'rb').read()

states = ("idle", "connecting", "listening", "thinking", "speaking", "interrupted", "muted")
expressions = ("neutral", "happy", "curious", "thinking", "concerned", "surprised", "apologetic", "confident")
visemes = ["sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS", "nn", "RR", "aa", "E", "I", "O", "U"]

poses = {s: [] for s in states}
poses["reduced_motion"] = []
poses.update({"expr_" + e: [] for e in expressions})
poses.update({"viseme_" + v: [] for v in visemes})
bindings = {
    "schemaVersion": "1.0", "renderer": "skia", "poses": poses, "animations": {}, "parameters": {},
    "channelMasks": {}, "framing": {"safeInset": 0.04, "fit": "contain"},
}
manifest = {
    "schemaVersion": "1.0", "id": "aurora-flow", "displayName": "Aurora Flow", "assetVersion": "1.0.0",
    "minRuntimeVersion": "1.0.0", "profile": "ambient",
    "representations": [{
        "id": "skia-shader", "renderer": "skia", "format": "sksl",
        "model": "models/aurora-flow.sksl", "bindings": "bindings/skia.json",
        "capabilities": ["ambient", "expressions", "audioReactive", "reducedMotion", "themeSlots", "speechArticulation"],
        "platforms": ["android", "ios", "maccatalyst", "windows"],
        "idleOwner": "scheduler", "blinkOwner": "none", "gazeOwner": "scheduler",
    }],
    "states": {s: {"pose": s, "transitionMs": 90 if s == "interrupted" else 320, "inputReactive": s == "listening",
                   "outputReactive": s == "speaking", "gaze": "processing" if s == "thinking" else "engaged"} for s in states},
    "expressions": {e: {"pose": "expr_" + e, "channels": ["color"], "transitionMs": 400} for e in expressions},
    "speech": {"mode": "canonicalVisemes", "canonicalProfile": "oculus15-v1",
               "mapping": {str(i): "viseme_" + v for i, v in enumerate(visemes)}, "expressionMouthScale": 1, "releaseMs": 90},
    "motions": {"idleVariants": [], "gestures": {}, "seedable": True},
    "themes": {"slots": {
        "cyan": {"light": "#00B8D9", "dark": "#41EFFF", "bindings": ["uniform:cyan"]},
        "blue": {"light": "#3676E8", "dark": "#5097FF", "bindings": ["uniform:blue"]},
        "violet": {"light": "#8F5BDF", "dark": "#A075FF", "bindings": ["uniform:violet"]},
        "pink": {"light": "#E255B8", "dark": "#FF6BD5", "bindings": ["uniform:pink"]},
        "warm": {"light": "#F08A3C", "dark": "#FFA05C", "bindings": ["uniform:warm"]},
    }},
    "reducedMotion": {"pose": "reduced_motion", "retainSpeech": True, "transitionMs": 180},
    "posters": {"light": "previews/poster-light.png", "dark": "previews/poster-dark.png"},
    "license": "LICENSE.txt", "provenance": "provenance.json",
}

def png_1x1():
    chunk = lambda t, d: struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d))
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', 1, 1, 8, 6, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(b'\x00\x00\x00\x00\x00')) + chunk(b'IEND', b'')

files = {
    "models/aurora-flow.sksl": shader,
    "bindings/skia.json": json.dumps(bindings, indent=1).encode(),
    "previews/poster-light.png": open(posters[0], 'rb').read() if posters else png_1x1(),
    "previews/poster-dark.png": open(posters[1], 'rb').read() if posters else png_1x1(),
    "LICENSE.txt": b"Written for the Spine Avatar Lab. Same licence as the Maui.Spine repository.\n",
    "provenance.json": json.dumps({"model": "hand-written SkSL shader (source/aurora_flow.sksl)",
                                   "posters": "rendered by the lab's skia surface" if posters else "placeholder 1x1, not a render"}, indent=1).encode(),
    "source/aurora_flow.py": open(__file__, 'rb').read(),
    "source/aurora_flow.sksl": shader,
}
manifest["files"] = [{"path": p, "sha256": hashlib.sha256(b).hexdigest(), "bytes": len(b)} for p, b in sorted(files.items())]
out = io.BytesIO()
with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as z:
    z.writestr("avatar.json", json.dumps(manifest, indent=1))
    for p, b in sorted(files.items()):
        z.writestr(p, b)
open(os.path.join(out_dir, "aurora-flow.spineavatar"), 'wb').write(out.getvalue())
print(os.path.join(out_dir, "aurora-flow.spineavatar"), len(out.getvalue()), "bytes")
