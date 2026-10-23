"""Build a signed phone helper from a verified Android cache candidate.

Needs JDK 17+, Android SDK 35/build-tools 35 and Gradle 8.9 on PATH or --gradle.
Private signing material is kept outside the source project and never exported.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
from android_payload import previous_hashes

ROOT = Path(__file__).resolve().parents[1]

def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()

def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--manifest',type=Path,required=True)
    p.add_argument('--vanilla',type=Path,required=True)
    p.add_argument('--gradle',default='gradle')
    p.add_argument('--sdk',type=Path,default=Path(os.environ.get('ANDROID_HOME','C:/Android/sdk')))
    p.add_argument('--out',type=Path,required=True)
    p.add_argument('--build-dir',type=Path,help='Optional empty ASCII-only build directory (Windows AIDL limitation)')
    a=p.parse_args()
    m=json.loads(a.manifest.read_text('utf8'))
    patch=Path(m['candidate'])
    assert m['package']=='com.feimo.astralparty' and m['appVersion']=='3.3.0'
    assert m['bundleRelativePath']=='com.unity.addressables/AssetBundles/4da1f23c112208b0f8df2a1327b4e4ce/3c7700aa58877b365d45231b1225fcbb/__data'
    assert sha(patch)==m['candidateSha256'] and sha(a.vanilla)==m['originalSha256']
    source=ROOT/'AndroidInstaller'
    assert (source/'app/src/main/assets/update-public-cert.txt').read_bytes() == (ROOT/'tools/update-public-cert.txt').read_bytes(), 'Desktop and Android update identities differ'
    if a.build_dir:
        project=a.build_dir.resolve()
        if project.exists() and any(project.iterdir()): raise RuntimeError('Build directory must be empty')
        project.mkdir(parents=True,exist_ok=True)
    else:
        import tempfile
        cache=Path(os.environ['LOCALAPPDATA'])/'XingShu/Build'
        cache.mkdir(parents=True,exist_ok=True)
        project=Path(tempfile.mkdtemp(prefix='android-installer-',dir=cache))
    # AIDL on Windows misdecodes non-ASCII paths; build a copy, keep source in place.
    if os.name=='nt' and not str(project).isascii(): raise RuntimeError('Select an ASCII-only --build-dir')
    shutil.copytree(source,project,dirs_exist_ok=True,ignore=shutil.ignore_patterns('build','.gradle','local.properties'))
    generated=project/'app/build/generated/payload'
    assets=generated/'assets'; assets.mkdir(parents=True,exist_ok=True)
    shutil.copy2(patch,assets/'candidate.__data'); shutil.copy2(a.vanilla,assets/'vanilla.__data')
    java=generated/'java/app/xingshu/installer/PayloadSpec.java'; java.parent.mkdir(parents=True,exist_ok=True)
    # Only previously published, verified preview4; unknown cache variants require official repair.
    known=previous_hashes(m)
    constants={'RELATIVE':m['bundleRelativePath'],'VERSION':m['runtimeVersion'], 'VANILLA':m['originalSha256'],'PATCH':m['candidateSha256']}
    java.write_text('package app.xingshu.installer;\nfinal class PayloadSpec {\n'+''.join('static final String '+k+' = '+json.dumps(v)+';\n' for k,v in constants.items())+'static final String[] PREVIOUS = {'+','.join(json.dumps(v) for v in known)+'};\n}\n',encoding='utf8')
    env=os.environ.copy(); env['ANDROID_HOME']=str(a.sdk)
    print('Build directory:',project,flush=True)
    subprocess.run([a.gradle,'--no-daemon','--console=plain','assembleRelease'],cwd=project,env=env,check=True)
    unsigned=project/'app/build/outputs/apk/release/app-release-unsigned.apk'
    signing=Path(os.environ['LOCALAPPDATA'])/'XingShu/Signing'; signing.mkdir(parents=True,exist_ok=True)
    key=signing/'android-installer.jks'; secret=signing/'android-installer.password'
    if not key.exists():
        if secret.exists(): raise RuntimeError('Signing password exists without its key; inspect signing folder')
        import secrets
        secret.write_text(secrets.token_urlsafe(32),encoding='ascii')
        subprocess.run(['keytool','-genkeypair','-keystore',str(key),'-alias','xingshu','-keyalg','RSA','-keysize','3072',
                        '-validity','10000','-dname','CN=XingShu Android Installer','-storepass:file',str(secret),'-keypass:file',str(secret)],check=True)
    if not secret.exists(): raise RuntimeError('Missing local signing password; do not create a new key for an update')
    a.out.parent.mkdir(parents=True,exist_ok=True)
    aligned=a.out.with_suffix('.aligned.apk')
    bt=a.sdk/'build-tools/35.0.0'
    subprocess.run([str(bt/'zipalign.exe'),'-f','-p','4',str(unsigned),str(aligned)],check=True)
    subprocess.run([str(bt/'apksigner.bat'),'sign','--ks',str(key),'--ks-key-alias','xingshu','--ks-pass','file:'+str(secret),'--out',str(a.out),str(aligned)],check=True)
    subprocess.run([str(bt/'apksigner.bat'),'verify','--verbose','--print-certs',str(a.out)],check=True)
    aligned.unlink()
    Path(str(a.out)+'.sha256').write_text(sha(a.out)+'  '+a.out.name+'\n',encoding='utf8')
    print('READY:',a.out,sha(a.out))

if __name__=='__main__': main()
