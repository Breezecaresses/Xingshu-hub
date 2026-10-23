"""Offline IPC contract tests: no game/window control and no live Hub transport."""
import argparse
import json
import os
from pathlib import Path
import queue
import subprocess
import sys
import threading

ROOT=Path(__file__).resolve().parents[1]

def main():
    sys.stdout.reconfigure(encoding='utf-8')
    parser=argparse.ArgumentParser();parser.add_argument('--core',type=Path,default=ROOT/'AstralParty-Hub/src-tauri/target/release/xingshu-core.exe');parser.add_argument('--out',type=Path,default=ROOT/'XingShu-Dev/build/native-windows/core-tests');args=parser.parse_args()
    out=args.out.resolve();out.mkdir(parents=True,exist_ok=True);events=queue.Queue();checks=[]
    def check(condition, name):
        if not condition: raise AssertionError(name)
        checks.append(name)
    stderr=(out/'core.stderr.log').open('w',encoding='utf-8')
    process=subprocess.Popen([str(args.core.resolve()),'--stdio','--no-bridge','--data-dir',str(out/'database')],cwd=out,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=stderr,text=True,encoding='utf-8',creationflags=subprocess.CREATE_NO_WINDOW if os.name=='nt' else 0)
    def read():
        for line in process.stdout:
            try:events.put(json.loads(line))
            except Exception as e:events.put(e)
    threading.Thread(target=read,daemon=True).start()
    def receive():
        result=events.get(timeout=30)
        if isinstance(result,Exception):raise result
        return result
    sequence=0
    def call(command, payload=None):
        nonlocal sequence
        sequence+=1;process.stdin.write(json.dumps({'id':sequence,'command':command,'args':payload or {}},ensure_ascii=False)+'\n');process.stdin.flush()
        while True:
            result=receive()
            if 'event' in result:continue
            check(result['id']==sequence,'响应请求匹配 '+command)
            return result
    try:
        ready=receive();check(ready.get('event')=='core://ready','核心握手');status=ready['payload']
        check(status['webview'] is False and status['transport']=='stdio','无 WebView 的进程管道通信')
        check(status['bridgeError'] is not None,'离线测试未争用游戏端口')
        for command in ['native_status','get_game_library','get_runtime_sessions','get_primary_game','list_extension_targets','list_plugins','list_mods','list_backups','list_install_transactions','get_bridge_status','get_spectator_hand_payload','list_providers','get_diagnostic_report']:
            result=call(command);check(result['ok'],'只读命令 '+command)
            if command in ['get_game_library','get_runtime_sessions']:check(result['result']==[],'空数据库不生成示例 '+command)
            if command=='get_spectator_hand_payload':check(result['result'] is None,'没有游戏数据时不生成手牌')
        for command in ['login_spectator','start_spectating','send_bridge_test_event']:
            result=call(command,{'accountName':'test','roomId':'123'});check(not result['ok'] and '演示' in result['error'],'旧演示入口禁用 '+command)
        check(not call('unknown_command')['ok'],'未知命令拒绝')
        check(not call('configure_runtime_bindings',{'bindings':[{'platformId':'TAPTAP','playerId':-1,'role':'MAIN'}]})['ok'],'无效账号绑定拒绝')
        check(not call('configure_runtime_bindings',{'bindings':[{'platformId':'TAPTAP','playerId':10,'role':'MAIN'},{'platformId':'TAPTAP','playerId':10,'role':'USER_SPECTATOR'}]})['ok'],'同一账号不兼任主账号和观战')
        check(call('configure_runtime_bindings',{'bindings':[]})['ok'],'空绑定合法')
        check(not call('send_watch_target',{'connectionId':'missing','target':{'watchCode':'123456'},'expectedSpectatorPlayerId':42})['ok'],'不存在的观战连接拒绝')
        check(not call('install_package',{'archivePath':'Z:/missing.zip','installationId':'missing'})['ok'],'无效安装不写游戏文件')
        process.stdin.write('{bad json}\n');process.stdin.flush();check(receive()['ok'] is False,'损坏请求有明确错误')
        check(call('native_status')['ok'],'损坏请求后仍能继续通信')
        check(call('shutdown')['ok'],'核心正常退出')
        process.wait(timeout=10);check(process.returncode==0,'核心退出码正常')
        (out/'results.json').write_text(json.dumps({'passed':len(checks),'checks':checks},ensure_ascii=False,indent=2),'utf-8')
        print(f'Native core: {len(checks)} checks passed.')
    finally:
        if process.poll() is None:process.stdin.close();process.wait(timeout=10)
        stderr.close()

if __name__=='__main__':main()
