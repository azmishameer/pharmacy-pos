#!/usr/bin/env python3
"""Build a clean, self-contained Windows x64 release. Run on the developer machine."""
import datetime
import hashlib
import json
import os
import platform
from pathlib import Path
import shutil
import subprocess
import zipfile

root = Path(__file__).resolve().parents[1]
version = datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%d-%H%M%S')
package = root / 'artifacts' / f'pharmacy-pos-win-x64-{version}'
app = package / 'app'
package.mkdir(parents=True, exist_ok=False)

def run(args, cwd=root):
    # A Rosetta Python can otherwise install x64 npm bindings into an arm64 Mac checkout.
    if platform.system() == 'Darwin' and subprocess.run(['/usr/sbin/sysctl', '-n', 'hw.optional.arm64'], capture_output=True, text=True).stdout.strip() == '1':
        args = ['/usr/bin/arch', '-arm64'] + args
    subprocess.run(args, cwd=cwd, check=True)

npm = ['cmd', '/c', 'npm'] if os.name == 'nt' else ['npm']
run(npm + ['ci'], root / 'frontend')
run(npm + ['run', 'build'], root / 'frontend')
run(npm + ['run', 'lint'], root / 'frontend')
run(['dotnet', 'publish', 'backend/PharmacyPos.Api', '-c', 'Release', '-r', 'win-x64',
     '--self-contained', 'true', '--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false', '-p:PublishSingleFile=false', '-o', str(app)])
shutil.copytree(root / 'frontend/dist', app / 'wwwroot', dirs_exist_ok=True)
for source in (root / 'deployment/windows').iterdir():
    if source.is_file(): shutil.copy2(source, package / source.name)
shutil.copy2(root / 'docs/windows-deployment.md', package / 'README.md')
shutil.copy2(root / 'LICENSE', package / 'LICENSE')
shutil.copy2(root / 'docs/demo.md', package / 'DEMO.md')
files = {}
for path in sorted(package.rglob('*')):
    if path.is_file():
        if path.name == 'appsettings.Local.json' or path.suffix.lower() in {'.pfx', '.key', '.dump', '.pem'}:
            raise RuntimeError('Private installation data must not be in a release.')
        files[path.relative_to(package).as_posix()] = hashlib.sha256(path.read_bytes()).hexdigest()
(package / 'manifest.json').write_text(json.dumps({'version': version, 'runtime': 'win-x64', 'files': files}, indent=2))
archive = package.with_suffix('.zip')
with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as zipped:
    for path in sorted(package.rglob('*')):
        if path.is_file(): zipped.write(path, path.relative_to(package.parent))
archive.with_suffix('.zip.sha256').write_text(hashlib.sha256(archive.read_bytes()).hexdigest() + '  ' + archive.name + '\n')
print(f'Release ready: {archive}')
