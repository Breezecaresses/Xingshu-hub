"""Prepare reversible runtime updates from the currently installed containers."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import UnityPy

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'APX-Dev/build/runtime-truth-fix'
DLL = ROOT / 'APX-Dev/build/bin/Release/APX.hooks.dll'
TARGETS = {
    'STEAM_CN': Path(r'E:\Steam\steamapps\common\Astral Party\8vJXn6CN\AstralParty_CN_Data\com.unity.addressables\AssetBundles\57a198703b66364d5420d39c0d251747\6cc68f014947b66dea74130802768a37\__data'),
    'TAPTAP': Path(r'F:\TapTap\PC Games\524022\CN_TAPTAP_V3.3.0\吉星派对_Data\com.unity.addressables\AssetBundles\57a198703b66364d5420d39c0d251747\b7c4592a112e6cee19eecf1d5a0a7dfd\__data'),
}

def sha(data):
    return hashlib.sha256(data).hexdigest()

def assets(env):
    result = {}
    for obj in env.objects:
        if obj.type.name == 'TextAsset':
            data = obj.read()
            result[obj.path_id] = (data.m_Name, sha(data.m_Script.encode('utf8', errors='surrogateescape')))
    return result

def main():
    OUT.mkdir(parents=True, exist_ok=True)
    manifest = []
    dll = DLL.read_bytes()
    for platform, target in TARGETS.items():
        folder = OUT / platform
        folder.mkdir(exist_ok=True)
        original = target.read_bytes()
        backup = folder / ('__data.before.' + sha(original)[:12])
        if backup.exists() and sha(backup.read_bytes()) != sha(original):
            raise RuntimeError(f'{platform}: original changed since preparation')
        if not backup.exists():
            backup.write_bytes(original)
        env = UnityPy.load(original)
        before = assets(env)
        hooks = [o for o in env.objects if o.path_id == 99992000 and o.type.name == 'TextAsset']
        if len(hooks) != 1 or hooks[0].read().m_Name != 'APX.hooks.dll':
            raise RuntimeError(f'{platform}: expected installed hooks asset missing')
        hook = hooks[0].read()
        hook.m_Script = dll.decode('utf8', errors='surrogateescape')
        hook.save()
        runtime_objects = [o for o in env.objects if o.type.name == 'TextAsset' and o.read().m_Name == 'AstralParty.Runtime.dll']
        assert len(runtime_objects) == 1, 'Expected exactly one existing game runtime'
        runtime = runtime_objects[0].read()
        original_runtime = folder / 'AstralParty.Runtime.before.dll'
        patched_runtime = folder / 'AstralParty.Runtime.dll'
        original_runtime.write_bytes(runtime.m_Script.encode('utf8', errors='surrogateescape'))
        subprocess.run(['pwsh', '-NoProfile', '-File', str(ROOT / 'tools/patch_thinking_time.ps1'),
                        '-InputDll', str(original_runtime), '-OutputDll', str(patched_runtime)], check=True)
        runtime.m_Script = patched_runtime.read_bytes().decode('utf8', errors='surrogateescape')
        runtime.save()
        candidate = folder / '__data'
        candidate.write_bytes(env.file.save())
        after = assets(UnityPy.load(str(candidate)))
        assert before.keys() == after.keys()
        assert after[99992000] == ('APX.hooks.dll', sha(dll))
        changed = {99992000, runtime_objects[0].path_id}
        assert all(after[key] == value for key, value in before.items() if key not in changed)
        manifest.append({'platform': platform, 'target': str(target), 'candidate': str(candidate),
                         'backup': str(backup), 'beforeSha256': sha(original),
                         'candidateSha256': sha(candidate.read_bytes())})
        print(f'{platform}: prepared; {len(before)-2} other assemblies unchanged; hooks and three timer methods updated', flush=True)
    (OUT / 'deployment.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf8')

if __name__ == '__main__':
    main()
