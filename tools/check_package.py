# Authored with LLM assistance, changes must be reviewed and owned by a human.
# Initial version reviewed and owned by @kaldigo
"""Validate the archive and feed before publication."""
import argparse, hashlib, json, struct, zipfile
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--tag',required=True);a=p.parse_args()
root=Path(__file__).resolve().parents[1];out=root/'artifacts'
m=json.loads((out/'pluginmaster.json').read_text())[0]
assert m['InternalName']=='Questionable' and m['AssemblyVersion']==a.tag[1:] and m['DalamudApiLevel']==15
assert m['RepoUrl']=='https://github.com/kaldigo/Questionable' and m['AcceptsFeedback'] is False
assert m['IconUrl']=='https://raw.githubusercontent.com/kaldigo/Questionable/new-main/assets/icon.png'
url=f'https://github.com/kaldigo/Questionable/releases/download/{a.tag}/Questionable.zip'
assert all(m[k]==url for k in ['DownloadLinkInstall','DownloadLinkUpdate','DownloadLinkTesting'])
archive=out/'Questionable.zip'
assert (out/'SHA256SUMS.txt').read_text().split()==[hashlib.sha256(archive.read_bytes()).hexdigest(),archive.name]
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None
    assert json.loads(z.read('Questionable.json'))==m
    assert {'Questionable.dll','Questionable.Model.dll','QuestPaths.dll','GatheringPaths.dll','ECommons.dll','PunishLib.dll','WrathCombo.API.dll','LICENSE','FORK.md'}.issubset(z.namelist())
    assert any(n.startswith('Resources/') for n in z.namelist())
    assert 'SixLabors.ImageSharp.dll' not in z.namelist()
    icon=z.read('images/icon.png')
    assert icon[:8]==b'\x89PNG\r\n\x1a\n' and struct.unpack('>II',icon[16:24])==(512,512)
    assert not any(n.startswith('/') or '..' in Path(n).parts or n.endswith('.zip') for n in z.namelist())
print('Validated Questionable package', a.tag)
