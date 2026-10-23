"""Build a real WPF desktop distribution with a WebView-free Rust core."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import zipfile

ROOT=Path(__file__).resolve().parents[1]
HUB=ROOT/'AstralParty-Hub'

def run(args, cwd):
    env=dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1', CARGO_BUILD_JOBS='2')
    subprocess.run([str(v) for v in args],cwd=cwd,env=env,check=True)

def assets(out):
    from PIL import Image
    source=HUB/'public'
    catalog=json.loads((HUB/'src/shared/gameCatalog.generated.json').read_text('utf-8-sig'))
    paths=set(catalog['assets'].values())
    paths.update('/'+str(p.relative_to(source)).replace('\\','/') for p in source.joinpath('catalog/video').glob('*.mp4'))
    count=0
    for relative in sorted(paths):
        src=(source/relative.lstrip('/')).resolve()
        if source.resolve() not in src.parents or not src.is_file(): continue
        target=out/'Assets'/src.relative_to(source)
        if src.suffix.lower()=='.webp':
            target=target.with_suffix('.png');target.parent.mkdir(parents=True,exist_ok=True)
            if not target.exists() or target.stat().st_mtime < src.stat().st_mtime:
                with Image.open(src) as image: image.save(target,'PNG',compress_level=6)
        else:
            target.parent.mkdir(parents=True,exist_ok=True); shutil.copy2(src,target)
        count+=1
    print(f'Native assets: {count} files (original pixels; PNG decoding needs no system WebP codec).')

def main():
    sys.stdout.reconfigure(encoding='utf-8')
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out',type=Path,default=ROOT/'XingShu-Dev/build/native-windows/app')
    parser.add_argument('--dotnet',default=str(ROOT/'tools/dotnet10/dotnet.exe') if (ROOT/'tools/dotnet10/dotnet.exe').is_file() else 'dotnet')
    parser.add_argument('--skip-core',action='store_true');parser.add_argument('--skip-ui',action='store_true')
    parser.add_argument('--self-contained',action='store_true');parser.add_argument('--zip',action='store_true')
    args=parser.parse_args();out=args.out.resolve();out.mkdir(parents=True,exist_ok=True)
    if not args.skip_core:run(['cargo','build','--release','--no-default-features','--features','native-backend','--bin','xingshu-core'],HUB/'src-tauri')
    if not args.skip_ui:
        run([args.dotnet,'publish','XingShu.Native.csproj','-c','Release','-r','win-x64','--self-contained',str(args.self_contained).lower(),'-o',out,'--nologo'],HUB/'native-windows')
    (out/'Core').mkdir(exist_ok=True);shutil.copy2(HUB/'src-tauri/target/release/xingshu-core.exe',out/'Core/xingshu-core.exe')
    assets(out)
    shutil.copy2(ROOT/'NATIVE_WINDOWS.md',out/'NATIVE_WINDOWS.md')
    (out/'README.txt').write_text('星枢 Hub · Windows 原生候选版 0.2.0-native1\n\n运行 xingshu-hub.exe。请保留 Core 和 Assets 目录。\n首次切换前正常退出旧 Hub；原生版不会关闭其他进程或强占游戏通信。\n账号与战绩沿用 %APPDATA%\\com.xingshu.hub 数据库；原生设置单独保存。\n--offline 可仅浏览离线资料。首次连接游戏时请在账号页明确绑定账号。\n\n本版使用 WPF 和 Rust，不携带 WebView、React 或网页开发服务器。\n地图攻略按来源标注：森秋幻境含实战速查，其余地图为各自的配置机制与任务。\n自动跟随已接入并通过离线检查，真实在线观战尚未验收。\nSpine/WebM 播放和完整托管管理页尚未迁入；详情见 NATIVE_WINDOWS.md。\n本压缩包只含 Hub，不安装电脑或安卓游戏插件。\n','utf-8-sig')
    manifest={'version':'0.2.0-native1','ui':'WPF','core':'Rust','webview':False,'selfContained':args.self_contained,
        'files':[{'path':p.relative_to(out).as_posix(),'size':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for p in sorted(out.rglob('*')) if p.is_file() and p.name!='native-manifest.json']}
    (out/'native-manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n','utf-8')
    if args.zip:
        target=out.parent/'XingShu-Hub-Windows-Native-0.2.0-native1.zip'
        with zipfile.ZipFile(target,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
            for path in sorted(out.rglob('*')):
                if path.is_file():z.write(path,Path('星枢Hub原生版')/path.relative_to(out))
        print(f'Portable ZIP: {target} ({target.stat().st_size/1024/1024:.1f} MiB)')
    print(f'Native distribution: {out}')

if __name__=='__main__':main()
