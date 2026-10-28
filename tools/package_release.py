# Authored with LLM assistance, changes must be reviewed and owned by a human.
# Initial version reviewed and owned by @kaldigo
"""Package the built plugin; preserve upstream identity and embedded localization resources."""
import argparse, hashlib, json, re, zipfile
from pathlib import Path

def package(tag):
    if not re.fullmatch(r'v[0-9]+(?:\.[0-9]+){3}', tag):
        raise ValueError('Expected four-part release tag')
    root = Path(__file__).resolve().parents[1]
    build = root / 'Questionable/dist'
    m = json.loads((build/'Questionable.json').read_text(encoding='utf-8-sig'))
    if (m['InternalName'], m['AssemblyVersion'], m['DalamudApiLevel']) != ('Questionable', tag[1:], 15):
        raise ValueError('Built identity/version/API differs from release plan')
    url = f'https://github.com/kaldigo/Questionable/releases/download/{tag}/Questionable.zip'
    m.update(RepoUrl='https://github.com/kaldigo/Questionable', AcceptsFeedback=False,
             Description=m['Description'] + '\n\nMaintained fork with isolated BossMod Reborn support. Report fork issues to kaldigo/Questionable. Solo-duty native acceptance remains pending.',
             DownloadLinkInstall=url, DownloadLinkUpdate=url, DownloadLinkTesting=url,
             Changelog='Isolated Reborn adapter; upstream quest updates preserved. Development preview: in-game acceptance pending.')
    out = root/'artifacts'
    out.mkdir(exist_ok=True)
    required = ['Questionable.dll', 'Questionable.Model.dll', 'QuestPaths.dll', 'GatheringPaths.dll', 'ECommons.dll', 'PunishLib.dll', 'WrathCombo.API.dll']
    if any(not (build/f).is_file() for f in required):
        raise ValueError('Missing runtime dependency')
    with zipfile.ZipFile(out/'Questionable.zip', 'w', zipfile.ZIP_DEFLATED) as z:
        for f in sorted(build.glob('*.dll')):
            if f.name == 'SixLabors.ImageSharp.dll':
                raise ValueError('Stale ImageSharp dependency in build output')
            z.write(f, f.name)
        for f in sorted((build/'Resources').rglob('*')):
            if f.is_file():
                z.write(f, f.relative_to(build).as_posix())
        z.writestr('Questionable.json', json.dumps(m, indent=2)+'\n')
        z.write(root/'LICENSE', 'LICENSE')
        z.write(root/'FORK.md', 'FORK.md')
    feed = json.dumps([m], indent=2)+'\n'
    (root/'pluginmaster.json').write_text(feed, encoding='utf-8')
    (out/'pluginmaster.json').write_text(feed, encoding='utf-8')
    digest = hashlib.sha256((out/'Questionable.zip').read_bytes()).hexdigest()
    (out/'SHA256SUMS.txt').write_text(digest+'  Questionable.zip\n')

if __name__ == '__main__':
    p=argparse.ArgumentParser();p.add_argument('--tag', required=True);a=p.parse_args();package(a.tag)
