"""Prepare reversible runtime updates from the currently installed containers."""
import hashlib
import argparse
import copy
import json
from pathlib import Path
import shutil
import subprocess
import re
import UnityPy
from UnityPy.files.ObjectReader import ObjectReader

ROOT = Path(__file__).resolve().parents[1]
LEGACY = json.loads((ROOT / 'tools/legacy_runtime_names.json').read_text('utf8'))
OUT = ROOT / 'XingShu-Dev/build/runtime-truth-fix'
DLL = ROOT / 'XingShu-Dev/build/bin/Release/XingShu.Runtime.dll'
PROFILES = ROOT / 'AstralParty-Hub/src-tauri/src/xing_shu/platform/platform_profiles.json'
MSBUILD = Path(r'D:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe')
CSC = MSBUILD.parent / 'Roslyn/csc.exe'


def build_bootstrap():
    project = ROOT / 'XingShu-Dev/tools/RuntimeBootstrap/Bootstrap.csproj'
    subprocess.run([str(MSBUILD), str(project), '/t:Rebuild', '/nologo', '/v:q'], check=True)
    patcher = OUT / 'RuntimeBootstrapPatcher.exe'
    libraries = ROOT / 'XingShu-Dev/tools/lib'
    subprocess.run([str(CSC), '/nologo', '/out:' + str(patcher),
                    '/reference:' + str(libraries / 'Mono.Cecil.dll'),
                    '/reference:' + str(libraries / 'Mono.Cecil.Rocks.dll'),
                    str(ROOT / 'tools/patch_taptap_runtime.cs')], check=True)
    for name in ('Mono.Cecil.dll', 'Mono.Cecil.Rocks.dll'):
        shutil.copy2(libraries / name, OUT / name)
    return patcher, project.parent / 'bin/Bootstrap.dll'


def put_hooks(env, dll):
    hooks = [o for o in env.objects if o.path_id == 99992000]
    if hooks:
        if len(hooks) != 1 or hooks[0].type.name != 'TextAsset' or hooks[0].read().m_Name not in ('XingShu.Runtime.dll', LEGACY['assembly'] + '.dll'):
            raise RuntimeError('Hooks path ID collision')
        hook = hooks[0].read()
        bundle = next(o.read() for o in hooks[0].assets_file.objects.values() if o.type.name == 'AssetBundle')
        bundle.m_Container = [('XingShu.Runtime.dll' if entry.asset.m_PathID == 99992000 else name, entry)
                              for name, entry in bundle.m_Container]
        bundle.save()
    else:
        sample = next(o for o in env.objects if o.type.name == 'TextAsset')
        sf = sample.assets_file
        reader = ObjectReader(assets_file=sf, reader=sample.reader, path_id=99992000,
                              type_id=sample.type_id, serialized_type=sample.serialized_type,
                              class_id=sample.class_id, type=sample.type, byte_start=0,
                              byte_size=0, is_destroyed=False, is_stripped=False)
        hook = sample.read()
        hook.object_reader = reader
        sf.objects[99992000] = reader
        bundle = next(o.read() for o in sf.objects.values() if o.type.name == 'AssetBundle')
        entry = copy.copy(bundle.m_Container[0][1])
        entry.asset = copy.copy(entry.asset)
        entry.asset.m_PathID = 99992000
        bundle.m_Container.append(('XingShu.Runtime.dll', entry))
        bundle.save()
    hook.m_Name = 'XingShu.Runtime.dll'
    hook.m_Script = dll.decode('utf8', errors='surrogateescape')
    hook.save()

def sha(data):
    return hashlib.sha256(data).hexdigest()

def assets(env):
    result = {}
    for obj in env.objects:
        if obj.type.name == 'TextAsset':
            data = obj.read()
            result[obj.path_id] = (data.m_Name, sha(data.m_Script.encode('utf8', errors='surrogateescape')))
    return result

def write_inventory(manifest):
    """Keep Hub's file inventory in sync with the packaged runtime candidates."""
    profiles = json.loads(PROFILES.read_text(encoding='utf8'))
    source = (ROOT / 'XingShu-Dev/src/XingShu.Runtime/XingShu/Runtime/XingShuRuntimeHost.cs').read_text('utf8')
    version = re.search(r'#else\s+public const string RuntimeVersion = "([^"]+)"', source).group(1)
    inventory = [{'platform': item['platform'], 'version': version,
                  'sha256': item['candidateSha256'],
                  'bundleRelativePath': next(p['bundleRelativePath'] for p in profiles if p['platformId'] == item['platform'])}
                 for item in manifest]
    (ROOT / 'AstralParty-Hub/src-tauri/src/runtime_inventory.generated.json').write_text(
        json.dumps(inventory, ensure_ascii=False, indent=2) + '\n', encoding='utf8')


def main():
    global OUT, DLL
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=Path, default=OUT, help='Candidate directory; does not install files')
    parser.add_argument('--previous-manifest', type=Path, help='Previously verified installed candidates')
    parser.add_argument('--skip-inventory', action='store_true', help='Keep Hub deployment inventory unchanged')
    parser.add_argument('--hooks-dll', type=Path, default=DLL, help='Use an isolated runtime build without replacing the default build output')
    parser.add_argument('--platform', choices=['STEAM_CN', 'TAPTAP', 'BILIBILI', 'WEGAME'], action='append', help='Prepare only selected Windows channels')
    args = parser.parse_args()
    OUT = args.out.resolve()
    DLL = args.hooks_dll.resolve()
    OUT.mkdir(parents=True, exist_ok=True)
    manifest = []
    dll = DLL.read_bytes()
    patcher, bootstrap = build_bootstrap()
    previous_path = args.previous_manifest or OUT / 'deployment.json'
    previous = json.loads(previous_path.read_text(encoding='utf8')) if previous_path.exists() else []
    for profile in json.loads(PROFILES.read_text(encoding='utf8')):
        platform = profile['platformId']
        if platform not in (args.platform or ['STEAM_CN', 'TAPTAP', 'BILIBILI', 'WEGAME']):
            continue
        target = Path(profile['installRoot']) / profile['bundleRelativePath']
        folder = OUT / platform
        folder.mkdir(exist_ok=True)
        original = target.read_bytes()
        known_installed = any(
            p['target'] == str(target) and (
                p['candidateSha256'] == sha(original) or (
                    p['beforeSha256'] == sha(original) and Path(p['backup']).is_file()
                    and sha(Path(p['backup']).read_bytes()) == sha(original)
                )
            ) for p in previous
        )
        if sha(original).upper() != profile['containerSha256'].upper() and not known_installed:
            raise RuntimeError(f'{platform}: cache differs from selected current baseline; update compatibility data first')
        backup = folder / ('__data.before.' + sha(original)[:12])
        if backup.exists() and sha(backup.read_bytes()) != sha(original):
            raise RuntimeError(f'{platform}: original changed since preparation')
        if not backup.exists():
            backup.write_bytes(original)
        env = UnityPy.load(original)
        before = assets(env)
        runtime_objects = [o for o in env.objects if o.type.name == 'TextAsset' and o.read().m_Name == 'AstralParty.Runtime.dll']
        assert len(runtime_objects) == 1, 'Expected exactly one existing game runtime'
        runtime = runtime_objects[0].read()
        original_runtime = folder / 'AstralParty.Runtime.before.dll'
        patched_runtime = folder / 'AstralParty.Runtime.dll'
        original_runtime.write_bytes(runtime.m_Script.encode('utf8', errors='surrogateescape'))
        subprocess.run(['pwsh', '-NoProfile', '-File', str(ROOT / 'tools/check_runtime_compatibility.ps1'),
                        '-RuntimeDll', str(original_runtime), '-HooksDll', str(DLL)], check=True)
        boot_runtime = folder / 'AstralParty.Runtime.bootstrap.dll'
        subprocess.run([str(patcher), str(original_runtime), str(bootstrap), str(boot_runtime),
                        sha(original_runtime.read_bytes()), LEGACY['bootstrapType']], check=True)
        subprocess.run(['pwsh', '-NoProfile', '-File', str(ROOT / 'tools/patch_thinking_time.ps1'),
                        '-InputDll', str(boot_runtime), '-OutputDll', str(patched_runtime),
                        '-HooksDll', str(DLL)], check=True)
        runtime.m_Script = patched_runtime.read_bytes().decode('utf8', errors='surrogateescape')
        runtime.save()
        put_hooks(env, dll)
        candidate = folder / '__data'
        candidate.write_bytes(env.file.save())
        after = assets(UnityPy.load(str(candidate)))
        assert set(before) | {99992000} == set(after)
        assert after[99992000] == ('XingShu.Runtime.dll', sha(dll))
        changed = {99992000, runtime_objects[0].path_id}
        assert all(after[key] == value for key, value in before.items() if key not in changed)
        manifest.append({'platform': platform, 'resourceVersion': profile.get('resourceVersion'),
                         'target': str(target), 'candidate': str(candidate),
                         'backup': str(backup), 'beforeSha256': sha(original),
                         'candidateSha256': sha(candidate.read_bytes())})
        print(f'{platform}: prepared; {len(before)-len(set(before) & changed)} other assemblies unchanged; bootstrap, hooks, real-clock decisions and statistics observers updated', flush=True)
    (OUT / 'deployment.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf8')
    if not args.skip_inventory:
        write_inventory(manifest)

if __name__ == '__main__':
    main()
