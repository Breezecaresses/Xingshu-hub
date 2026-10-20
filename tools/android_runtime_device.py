"""Deploy/restore the Android preview using an explicit ADB serial and hash gates.

Only the verified external assembly cache is replaced. App data is never cleared.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import shlex
import subprocess

PACKAGE = "com.feimo.astralparty"
ROOT = f"/sdcard/Android/data/{PACKAGE}/files/"


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("install", "restore"))
    parser.add_argument("--serial", required=True)
    parser.add_argument("--manifest", required=True, type=Path)
    parser.add_argument("--backup-dir", required=True, type=Path)
    args = parser.parse_args()
    manifest = json.loads(args.manifest.read_text("utf8"))
    if manifest["package"] != PACKAGE or manifest["platform"] != "ANDROID_CN":
        raise RuntimeError("Unexpected package/platform")
    relative = manifest["bundleRelativePath"]
    if not re.fullmatch(r"com\.unity\.addressables/AssetBundles/[a-f0-9]{32}/[a-f0-9]{32}/__data", relative):
        raise RuntimeError("Unexpected cache path")
    target = ROOT + relative
    # Android may allow writing the existing cache but deny creating siblings.
    # Stage under shell-owned storage, then copy into the existing file while stopped.
    stage = "/data/local/tmp/xingshu-" + manifest["candidateSha256"] + ".bundle"
    backup_dir = args.backup_dir.resolve()
    backup_dir.mkdir(parents=True, exist_ok=True)
    backup = backup_dir / "assembly.vanilla.__data"
    receipt = backup_dir / "deployment.json"

    def adb(*argv):
        result = subprocess.run(["adb", "-s", args.serial, *argv],
                                capture_output=True, text=True, encoding="utf8", errors="replace")
        if result.returncode:
            raise RuntimeError((result.stderr or result.stdout).strip())
        return result.stdout.strip()

    def shell(*argv):
        return adb("shell", " ".join(shlex.quote(str(arg)) for arg in argv))

    def remote_sha(path):
        value = shell("sha256sum", path).split()[0].lower()
        if not re.fullmatch(r"[a-f0-9]{64}", value):
            raise RuntimeError("Invalid device hash response")
        return value

    if adb("get-state") != "device":
        raise RuntimeError("Device is not ready")
    package_info = shell("dumpsys", "package", PACKAGE)
    if "versionName=" + manifest["appVersion"] not in package_info:
        raise RuntimeError("Game app version differs from the verified candidate")
    current_sha = remote_sha(target)
    vanilla = manifest["originalSha256"]
    candidate_sha = manifest["candidateSha256"]
    if args.action == "install":
        source = Path(manifest["candidate"])
        if sha(source) != candidate_sha:
            raise RuntimeError("Candidate hash differs from manifest")
        if current_sha != vanilla:
            # Permit a second, verified preview only if this serial's receipt proves it.
            previous = json.loads(receipt.read_text("utf8")) if receipt.exists() else {}
            if (previous.get("serial") != args.serial or previous.get("target") != target
                    or previous.get("installedSha256") != current_sha):
                raise RuntimeError("Device cache is neither vanilla nor the recorded preview")
        if backup.exists():
            if sha(backup) != vanilla:
                raise RuntimeError("Existing vanilla backup is invalid")
        else:
            if current_sha != vanilla:
                raise RuntimeError("No verified vanilla backup available")
            adb("pull", target, str(backup))
            if sha(backup) != vanilla:
                raise RuntimeError("Pulled backup failed verification")
        expected = candidate_sha
    else:
        if not receipt.exists():
            raise RuntimeError("No deployment receipt; refusing to replace untracked data")
        previous = json.loads(receipt.read_text("utf8"))
        if (previous.get("serial") != args.serial or previous.get("target") != target
                or current_sha not in (vanilla, previous.get("installedSha256"))):
            raise RuntimeError("Device cache changed since recorded deployment")
        source = backup
        expected = vanilla
        if not source.exists() or sha(source) != vanilla:
            raise RuntimeError("Vanilla backup is missing or invalid")

    # Stage and verify before stopping the app or touching the live cache.
    adb("push", str(source), stage)
    if remote_sha(stage) != expected:
        raise RuntimeError("Staged bytes failed verification; live cache untouched")
    # Recheck immediately before replacement to avoid overwriting an official update.
    if remote_sha(target) != current_sha:
        raise RuntimeError("Game cache changed during staging; live cache untouched")
    shell("am", "force-stop", PACKAGE)
    try:
        shell("cp", stage, target)
        if remote_sha(target) != expected:
            raise RuntimeError("Installed hash mismatch")
    except Exception as deployment_error:
        adb("push", str(backup), stage)
        if remote_sha(stage) != vanilla:
            raise RuntimeError("Deployment and rollback staging failed; use the host vanilla backup") from deployment_error
        shell("cp", stage, target)
        if remote_sha(target) != vanilla:
            raise RuntimeError("Rollback verification failed; use the host vanilla backup") from deployment_error
        raise RuntimeError("Deployment failed; restored and verified vanilla cache") from deployment_error
    result = {"serial": args.serial, "package": PACKAGE, "target": target,
              "originalSha256": vanilla, "installedSha256": expected,
              "backup": str(backup), "action": args.action,
              "timestamp": datetime.now(timezone.utc).isoformat()}
    receipt.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf8")
    print(f"{args.action}: verified {expected}; game stopped, ready for normal launch")


if __name__ == "__main__":
    main()
