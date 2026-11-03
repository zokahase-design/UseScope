"""Assemble portable macOS .app ZIPs with Unix permissions on any build host."""
import argparse
import hashlib
import json
import plistlib
import shutil
import stat
import struct
import uuid
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

args = argparse.ArgumentParser()
args.add_argument('--root', type=Path, required=True)
args.add_argument('--publish', type=Path, required=True)
args.add_argument('--rid', choices=['osx-arm64', 'osx-x64'], required=True)
options = args.parse_args()
root, publish, rid = options.root.resolve(), options.publish.resolve(), options.rid
project = ET.parse(root / 'mac' / 'UseScope.Mac.csproj').getroot()
version = project.findtext('./PropertyGroup/Version')
stage = root / 'artifacts' / ('mac-bundle-' + rid + '-' + uuid.uuid4().hex)
app = stage / 'UseScope.app'
contents = app / 'Contents'
macos = contents / 'MacOS'
resources = contents / 'Resources'
macos.mkdir(parents=True)
resources.mkdir()
shutil.copytree(publish, macos, dirs_exist_ok=True)
info = {
    'CFBundleIdentifier': 'io.github.zokahase-design.usescope',
    'CFBundleName': 'UseScope', 'CFBundleDisplayName': 'UseScope',
    'CFBundleExecutable': 'UseScope', 'CFBundlePackageType': 'APPL',
    'CFBundleInfoDictionaryVersion': '6.0', 'CFBundleVersion': version,
    'CFBundleShortVersionString': version, 'CFBundleIconFile': 'UseScope.icns',
    'LSMinimumSystemVersion': '15.0', 'NSHighResolutionCapable': True, 'LSUIElement': True,
}
with (contents / 'Info.plist').open('wb') as target:
    plistlib.dump(info, target)
# ICNS permits an original PNG payload for its 512px icon representation.
png = (root / 'source' / 'Assets' / 'UseScope.png').read_bytes()
if png[:8] != b'\x89PNG\r\n\x1a\n' or struct.unpack('>II', png[16:24]) != (512, 512):
    raise ValueError('Expected the original 512px PNG product mark')
chunk = b'ic09' + struct.pack('>I', len(png) + 8) + png
(resources / 'UseScope.icns').write_bytes(b'icns' + struct.pack('>I', len(chunk) + 8) + chunk)
docs = stage / 'docs'
licenses = docs / 'licenses'
licenses.mkdir(parents=True)
for source, destination in [('LICENSE', 'LICENSE.txt'), ('THIRD_PARTY_NOTICES.md', 'THIRD_PARTY_NOTICES.md'), ('docs/MACOS.md', 'README.md'), ('docs/PRIVACY.md', 'PRIVACY.md'), ('docs/PROVIDERS.md', 'PROVIDERS.md'), ('docs/BRAND_ASSETS.md', 'BRAND_ASSETS.md')]:
    shutil.copyfile(root / source, docs / destination)
for source in (root / 'docs' / 'licenses').glob('*.txt'):
    shutil.copyfile(source, licenses / source.name)

assets = json.loads((root / 'mac' / 'obj' / 'project.assets.json').read_text(encoding='utf-8-sig'))
package_roots = [Path(path) for path in assets['packageFolders']]
dependencies = json.loads((publish / 'UseScope.deps.json').read_text(encoding='utf-8-sig'))
components = []
for name, metadata in dependencies['libraries'].items():
    package_name, package_version = name.rsplit('/', 1)
    actual = package_name.removeprefix('runtimepack.')
    if actual == 'UseScope':
        continue
    package = next((path / actual.lower() / package_version for path in package_roots
                    if (path / actual.lower() / package_version).is_dir()), None)
    if package is None:
        raise FileNotFoundError('Cannot locate license metadata for ' + name)
    copied = []
    for file in package.iterdir():
        if file.is_file() and any(term in file.name.lower() for term in ['license', 'third-party', 'notice', 'copying']):
            target = actual + '-' + package_version + '-' + file.name
            shutil.copyfile(file, licenses / target)
            copied.append(target)
    nuspec = package / (actual.lower() + '.nuspec')
    if nuspec.is_file():
        shutil.copyfile(nuspec, licenses / (actual + '-' + package_version + '.nuspec'))
    if not copied and not actual.startswith('Avalonia') and actual != 'MicroCom.Runtime':
        raise ValueError('Missing full license text for ' + name)
    components.append({'Package': actual, 'Version': package_version, 'IncludedNotices': copied})

executable = macos / 'UseScope'
header = executable.read_bytes()[:8]
expected_cpu = 0x0100000C if rid == 'osx-arm64' else 0x01000007
if header[:4] != b'\xcf\xfa\xed\xfe' or struct.unpack('<I', header[4:8])[0] != expected_cpu:
    raise ValueError('The app host is not the requested macOS architecture')
(docs / 'build-info.json').write_text(json.dumps({
    'Application': 'UseScope', 'Version': version, 'Architecture': rid,
    'MinimumMacOS': '15.0', 'Signed': False, 'Notarized': False,
    'InteractiveMacOSTested': False, 'Components': components,
}, indent=2), encoding='utf-8')
# Keep notices inside the .app as well: users commonly move only the bundle.
shutil.copytree(licenses, resources / 'licenses')
shutil.copyfile(docs / 'LICENSE.txt', resources / 'LICENSE.txt')
shutil.copyfile(docs / 'THIRD_PARTY_NOTICES.md', resources / 'THIRD_PARTY_NOTICES.md')
manifest = []
for path in sorted(stage.rglob('*')):
    if path.is_file():
        manifest.append({'Path': path.relative_to(stage).as_posix(), 'Bytes': path.stat().st_size,
                         'SHA256': hashlib.sha256(path.read_bytes()).hexdigest()})
(docs / 'manifest.json').write_text(json.dumps({'Version': version, 'Files': manifest}, indent=2), encoding='utf-8')
dist = root / 'dist'
dist.mkdir(exist_ok=True)
archive = dist / f'UseScope-{version}-{rid}-preview.zip'
with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as target:
    for path in sorted(stage.rglob('*')):
        if not path.is_file():
            continue
        relative = path.relative_to(stage).as_posix()
        entry = zipfile.ZipInfo(relative)
        entry.create_system = 3
        magic = path.read_bytes()[:4]
        executable_file = magic in (b'\xcf\xfa\xed\xfe', b'\xfe\xed\xfa\xcf', b'\xca\xfe\xba\xbe', b'\xbe\xba\xfe\xca')
        entry.external_attr = (stat.S_IFREG | (0o755 if executable_file else 0o644)) << 16
        entry.compress_type = zipfile.ZIP_DEFLATED
        target.writestr(entry, path.read_bytes())
digest = hashlib.sha256(archive.read_bytes()).hexdigest()
(dist / (archive.name + '.sha256')).write_text(digest + '  ' + archive.name + '\n', encoding='utf-8')
with zipfile.ZipFile(archive) as check:
    app_entry = check.getinfo('UseScope.app/Contents/MacOS/UseScope')
    if (app_entry.external_attr >> 16) & 0o111 != 0o111:
        raise ValueError('The ZIP did not preserve app-host execute permissions')
    for entry in check.infolist():
        if check.read(entry)[:4] in (b'\xcf\xfa\xed\xfe', b'\xfe\xed\xfa\xcf', b'\xca\xfe\xba\xbe', b'\xbe\xba\xfe\xca') and not (entry.external_attr >> 16) & 0o111:
            raise ValueError('Missing execute permissions: ' + entry.filename)
    if 'UseScope.app/Contents/Resources/LICENSE.txt' not in check.namelist():
        raise ValueError('Application license must travel inside the .app bundle')
    if check.testzip() is not None:
        raise ValueError('ZIP integrity failure')
    for item in manifest:
        if hashlib.sha256(check.read(item['Path'])).hexdigest() != item['SHA256']:
            raise ValueError('Archive payload hash mismatch')
report = {'Passed': True, 'Architecture': rid, 'MacHostArchitectureVerified': True,
          'ExecutePermissionsVerified': True, 'ManifestFilesVerified': len(manifest),
          'Signed': False, 'InteractiveMacOSTested': False, 'Archive': archive.name, 'SHA256': digest}
(root / 'artifacts' / ('mac-package-test-' + rid + '.json')).write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report, indent=2))
