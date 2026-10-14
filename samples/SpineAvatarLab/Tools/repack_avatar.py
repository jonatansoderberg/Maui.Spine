"""Replaces files in a .spineavatar and updates the manifest's SHA-256 and sizes.

Usage: python repack_avatar.py <in.spineavatar> <out.spineavatar> [--id ID --name NAME] <archive/path>=<local file> [...]
Used by the lab (#524) to adjust bindings or models of reference avatars without regenerating them;
the change is recorded in source/lab-changes.md inside the archive.
"""
import hashlib, io, json, sys, zipfile

source, target, *rest = sys.argv[1:]
options = {}
while rest and rest[0].startswith('--'):
    options[rest[0][2:]] = rest[1]
    rest = rest[2:]
replace = dict(p.split('=', 1) for p in rest)

with zipfile.ZipFile(source) as z:
    files = {n: z.read(n) for n in z.namelist() if not n.endswith('/')}

notes = files.get('source/lab-changes.md', b'').decode()
for path, local in replace.items():
    if path not in files:
        raise SystemExit(f'{path} is not in {source}')
    files[path] = open(local, 'rb').read()
    notes += f'- {path} replaced by the Spine Avatar Lab (#524)\n'
files['source/lab-changes.md'] = notes.encode()

manifest = json.loads(files.pop('avatar.json'))
if 'id' in options:
    manifest['id'] = options['id']
if 'name' in options:
    manifest['displayName'] = options['name']
manifest['files'] = [{'path': p, 'sha256': hashlib.sha256(b).hexdigest(), 'bytes': len(b)} for p, b in sorted(files.items())]

out = io.BytesIO()
with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as z:
    z.writestr('avatar.json', json.dumps(manifest, indent=1))
    for p, b in sorted(files.items()):
        z.writestr(p, b)
open(target, 'wb').write(out.getvalue())
print(target, len(out.getvalue()), 'bytes')
