# relight.py in.spineavatar out.spineavatar NAME EMISSIVE BASE ROUGH : one material lit by the scene instead of mostly
# emissive (the generator's 0.9 emissive + 0.1 base flattens the hair: every card edge shows). Package otherwise unchanged.
import sys, json, struct, zipfile, hashlib
src, dst, name, em, base, rough = sys.argv[1], sys.argv[2], sys.argv[3], float(sys.argv[4]), float(sys.argv[5]), float(sys.argv[6])
z = zipfile.ZipFile(src); files = {n: z.read(n) for n in z.namelist()}
b = files['models/kairo.glb']; n = struct.unpack('<I', b[12:16])[0]; g = json.loads(b[20:20 + n]); rest = b[20 + n:]
for m in g['materials']:
    if m['name'] == name:
        m['pbrMetallicRoughness']['baseColorFactor'] = [base, base, base, 1]
        m['pbrMetallicRoughness']['roughnessFactor'] = rough
        m['emissiveFactor'] = [em, em, em]
        print('relit', name, m)
js = json.dumps(g, separators=(',', ':')).encode(); js += b' ' * (-len(js) % 4)
glb = struct.pack('<III', 0x46546C67, 2, 12 + 8 + len(js) + len(rest)) + struct.pack('<II', len(js), 0x4E4F534A) + js + rest
files['models/kairo.glb'] = glb
man = json.loads(files['avatar.json'])
for f in man['files']:
    f['sha256'] = hashlib.sha256(files[f['path']]).hexdigest(); f['bytes'] = len(files[f['path']])
files['avatar.json'] = json.dumps(man, indent=1).encode()
with zipfile.ZipFile(dst, 'w', zipfile.ZIP_DEFLATED) as o:
    o.writestr('avatar.json', files.pop('avatar.json'))
    for k, v in sorted(files.items()): o.writestr(k, v)
