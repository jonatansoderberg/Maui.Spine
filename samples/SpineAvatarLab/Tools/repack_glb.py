"""Rebuilds a GLB with replaced images; geometry, skins and morph targets are copied byte for byte."""
import json, struct, sys
src, dst, *pairs = sys.argv[1:]
b = open(src, 'rb').read()
l = struct.unpack('<I', b[12:16])[0]
j = json.loads(b[20:20 + l])
bs = 20 + l + 8
bin_ = b[bs:]
replace = {}
for p in pairs:
    index, path = p.split('=', 1)
    replace[int(index)] = path
image_views = {img['bufferView']: i for i, img in enumerate(j['images'])}
out = bytearray()
for v, view in enumerate(j['bufferViews']):
    while len(out) % 4: out += b'\0'
    if v in image_views and image_views[v] in replace:
        path = replace[image_views[v]]
        data = open(path, 'rb').read()
        j['images'][image_views[v]]['mimeType'] = 'image/jpeg' if path.endswith('.jpg') else 'image/png'
    else:
        o = view.get('byteOffset', 0)
        data = bin_[o:o + view['byteLength']]
    view['byteOffset'] = len(out)
    view['byteLength'] = len(data)
    out += data
while len(out) % 4: out += b'\0'
j['buffers'] = [{'byteLength': len(out)}]
js = json.dumps(j, separators=(',', ':')).encode()
while len(js) % 4: js += b' '
glb = struct.pack('<III', 0x46546C67, 2, 12 + 8 + len(js) + 8 + len(out)) + struct.pack('<II', len(js), 0x4E4F534A) + js + struct.pack('<II', len(out), 0x004E4942) + out
open(dst, 'wb').write(glb)
print(dst, len(glb), 'bytes')
