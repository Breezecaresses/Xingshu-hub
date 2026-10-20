"""Build a managed Android preview from a verified vanilla Android cache bundle.

Does not write to a device, alter PC runtime inventory, or package Windows natives.
"""
import argparse
import json
from pathlib import Path
import subprocess

import UnityPy
import prepare_runtime_truth_fix as shared

ROOT = Path(__file__).resolve().parents[1]
VANILLA_SHA256 = "01ee8b4332fdb024fd4c5b9bfdb38ce25f9d42e0b07941765bc7cd06efdaaaa4"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle", required=True, type=Path)
    parser.add_argument("--out", required=True, type=Path)
    args = parser.parse_args()
    original = args.bundle.read_bytes()
    if shared.sha(original) != VANILLA_SHA256:
        raise RuntimeError("Unknown Android baseline; verify version and compatibility before patching")
    out = args.out.resolve()
    out.mkdir(parents=True, exist_ok=True)
    env = UnityPy.load(original)
    runtime_objects = [obj for obj in env.objects if obj.type.name == "TextAsset"
                       and obj.read().m_Name == "AstralParty.Runtime.dll"]
    if len(runtime_objects) != 1:
        raise RuntimeError("Expected exactly one runtime assembly")
    runtime_obj = runtime_objects[0]
    if int(runtime_obj.assets_file.target_platform) != 13:
        raise RuntimeError("Input is not an Android serialized file")
    before = shared.assets(env)
    runtime = runtime_obj.read()
    runtime_path = out / "AstralParty.Runtime.original.dll"
    runtime_path.write_bytes(runtime.m_Script.encode("utf8", errors="surrogateescape"))
    bin_dir = out / "bin"
    subprocess.run([str(shared.MSBUILD), str(ROOT / "APX-Dev/src/APX.hooks/APX.hooks.csproj"),
                    "/t:Rebuild", "/nologo", "/v:q", "/p:Configuration=Release",
                    "/p:AdditionalDefineConstants=XINGSHU_ANDROID_PREVIEW",
                    "/p:AstralPartyRuntimePath=" + str(runtime_path),
                    "/p:OutputPath=" + str(bin_dir) + "/",
                    "/p:IntermediateOutputPath=" + str(out / "obj") + "/"], check=True)
    hooks_path = bin_dir / "APX.hooks.dll"
    hooks = hooks_path.read_bytes()
    subprocess.run(["pwsh", "-NoProfile", "-File", str(ROOT / "tools/check_android_runtime.ps1"),
                    "-HooksDll", str(hooks_path)], check=True)
    subprocess.run(["pwsh", "-NoProfile", "-File", str(ROOT / "tools/check_runtime_compatibility.ps1"),
                    "-RuntimeDll", str(runtime_path), "-HooksDll", str(hooks_path)], check=True)
    # Redirect the shared compiler output; never change the desktop deployment inventory.
    shared.OUT = out / "bootstrap-tool"
    shared.OUT.mkdir(exist_ok=True)
    patcher, bootstrap = shared.build_bootstrap()
    boot_path = out / "AstralParty.Runtime.bootstrap.dll"
    patched_path = out / "AstralParty.Runtime.dll"
    subprocess.run([str(patcher), str(runtime_path), str(bootstrap), str(boot_path),
                    shared.sha(runtime_path.read_bytes())], check=True)
    subprocess.run(["pwsh", "-NoProfile", "-File", str(ROOT / "tools/patch_thinking_time.ps1"),
                    "-InputDll", str(boot_path), "-OutputDll", str(patched_path),
                    "-HooksDll", str(hooks_path)], check=True)
    subprocess.run(["pwsh", "-NoProfile", "-File", str(ROOT / "tools/verify_thinking_time.ps1"),
                    "-Before", str(boot_path), "-After", str(patched_path)], check=True)
    runtime.m_Script = patched_path.read_bytes().decode("utf8", errors="surrogateescape")
    runtime.save()
    shared.put_hooks(env, hooks)
    candidate = out / "__data"
    candidate.write_bytes(env.file.save())
    verified = UnityPy.load(str(candidate))
    after = shared.assets(verified)
    assert set(before) | {99992000} == set(after), "Unexpected assets added/removed"
    assert after[99992000] == ("APX.hooks.dll", shared.sha(hooks))
    assert all(after[key] == value for key, value in before.items() if key != runtime_obj.path_id)
    assert all(int(obj.assets_file.target_platform) == 13 for obj in verified.objects)
    manifest = {"platform": "ANDROID_CN", "package": "com.feimo.astralparty",
                "appVersion": "3.3.0", "runtimeVersion": "1.0.0-android-preview1",
                "originalSha256": shared.sha(original),
                "candidateSha256": shared.sha(candidate.read_bytes()),
                "hooksSha256": shared.sha(hooks), "candidate": str(candidate),
                "unchangedTextAssets": len(before) - 1,
                "bundleRelativePath": "com.unity.addressables/AssetBundles/4da1f23c112208b0f8df2a1327b4e4ce/3c7700aa58877b365d45231b1225fcbb/__data",
                "nativeLibraries": [], "desktopHubBridgeVerified": False}
    (out / "candidate.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf8")
    print(f"Android candidate verified: {len(before) - 1} other TextAssets unchanged; no native libraries")


if __name__ == "__main__":
    main()
