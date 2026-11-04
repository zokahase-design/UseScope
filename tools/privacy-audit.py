"""Check distributable files without printing matched private values. Standard library only."""
import argparse
import json
import re
import zipfile
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--root', type=Path)
parser.add_argument('--zip', type=Path)
parser.add_argument('--source', action='store_true', help='Exclude build and Git directories')
parser.add_argument('--report', type=Path, required=True)
args = parser.parse_args()
if bool(args.root) == bool(args.zip):
    parser.error('Choose exactly one of --root or --zip')

patterns = {
    'Provider key': re.compile(rb'\bsk-(?:proj-|ant-)?[A-Za-z0-9_-]{20,}\b'),
    'GitHub token': re.compile(rb'\b(?:github_pat_[A-Za-z0-9_]{30,}|gh[pousr]_[A-Za-z0-9]{30,})\b'),
    'JWT': re.compile(rb'\beyJ[A-Za-z0-9_-]{12,}\.[A-Za-z0-9_-]{12,}\.[A-Za-z0-9_-]{8,}\b'),
    'Private key': re.compile(rb'-----BEGIN (?:[A-Z]+ )?PRIVATE KEY-----\s+[A-Za-z0-9+/=\r\n]{64,}'),
    'Windows user path': re.compile(rb'(?i)\b[A-Z]:[\\/]Users[\\/](?!Public[\\/]|Default[\\/]|<)[^\\/\r\n"<>]+[\\/]'),
    'Mac user path': re.compile(rb'/Users/(?!<|Shared/)[A-Za-z0-9_.-]+/'),
}
email = re.compile(rb'[A-Za-z0-9._%+-]+@([A-Za-z0-9.-]+\.[A-Za-z]{2,})')
private_name = re.compile(r'(?i)^(?:auth\.json|\.credentials\.json|api-key\.dpapi(?:\..*)?|settings\.json(?:\..*)?|\.env(?:\..*)?|cookies(?:\..*)?|usage(?:-.*)?\.jsonl)$')
excluded = {'.git', 'artifacts', 'dist', 'bin', 'obj', '.vs', '.idea', '.vscode', '__pycache__'}
findings = []
scanned = 0
upstream_build_metadata = []
vendor_native_libraries = {'libAvaloniaNative.dylib', 'libclrjit.dylib', 'libcoreclr.dylib', 'libmscordaccore.dylib', 'libmscordbi.dylib'}

def inspect(name, data):
    global scanned
    scanned += 1
    path = Path(name)
    if private_name.fullmatch(path.name) or any(part.lower() == 'profiles' for part in path.parts) or path.suffix.lower() in {'.lnk', '.log', '.dmp', '.pdb', '.pfx', '.p12', '.pem', '.key', '.bak'}:
        findings.append({'File': name, 'Kind': 'Private file or debug artifact'})
    # UTF-16 literals in managed executables are checked as well as UTF-8 text.
    for payload in (data, data.replace(b'\x00', b'')):
        for kind, pattern in patterns.items():
            if kind == 'Mac user path' and path.name in vendor_native_libraries:
                # These unchanged upstream runtime binaries embed GitHub's build-worker root.
                # No credential rules are suppressed, and other user roots still fail.
                payload_for_rule = payload.replace(b'/Users/' + b'runner/', b'/_upstream_build_/')
                if payload_for_rule != payload and name not in upstream_build_metadata:
                    upstream_build_metadata.append(name)
            else:
                payload_for_rule = payload
            if pattern.search(payload_for_rule):
                finding = {'File': name, 'Kind': kind}
                if finding not in findings:
                    findings.append(finding)
        # Third-party legal notices legitimately contain their authors' public addresses.
        if 'licenses' not in path.parts and path.suffix.lower() not in {'.dll', '.dylib', '.so', '.exe', '.png', '.ico', '.icns'}:
            for match in email.finditer(payload):
                domain = match.group(1).decode('ascii').lower()
                if domain.endswith(('.example', '.invalid', '.test', '.example.com', '.example.org', '.example.net', 'users.noreply.github.com')) or domain in {'example.com', 'example.org', 'example.net'}:
                    continue
                finding = {'File': name, 'Kind': 'Non-example email requires review'}
                if finding not in findings:
                    findings.append(finding)

if args.root:
    import os
    base = args.root.resolve()
    for parent, directories, files in os.walk(base, followlinks=False):
        if args.source:
            directories[:] = [directory for directory in directories if directory not in excluded]
        for file in files:
            path = Path(parent) / file
            if path.is_symlink():
                findings.append({'File': path.relative_to(base).as_posix(), 'Kind': 'Unexpected symbolic link'})
                continue
            inspect(path.relative_to(base).as_posix(), path.read_bytes())
else:
    with zipfile.ZipFile(args.zip) as archive:
        for entry in archive.infolist():
            if not entry.is_dir():
                if '..' in Path(entry.filename).parts or entry.filename.startswith(('/', '\\')):
                    findings.append({'File': entry.filename, 'Kind': 'Unsafe archive path'})
                inspect(entry.filename, archive.read(entry))

result = {'Passed': not findings, 'FilesScanned': scanned, 'Findings': findings, 'UpstreamBuildMetadata': upstream_build_metadata}
args.report.parent.mkdir(parents=True, exist_ok=True)
args.report.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
print(json.dumps(result, indent=2))
raise SystemExit(0 if result['Passed'] else 2)
