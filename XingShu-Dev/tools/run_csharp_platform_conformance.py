import os
import subprocess
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

workspace = r"E:\工作流\星引擎插件"
dotnet_bin = "dotnet"
csc_dll = r"C:\Program Files\dotnet\sdk\7.0.304\Roslyn\bincore\csc.dll"

ref_base = os.path.join(workspace, "XingShu-Dev", "references")
mscorlib = os.path.join(ref_base, "framework", "mscorlib.dll")
system_dll = os.path.join(ref_base, "unity", "System.dll")
system_core = os.path.join(ref_base, "framework", "System.Core.dll")
xingshu_hooks = os.path.join(workspace, "XingShu-Dev", "build", "bin", "Release", "XingShu.Runtime.dll")

out_dir = os.path.join(workspace, "XingShu-Dev", "tools", "PlatformProfileConformanceRunner", "bin")
os.makedirs(out_dir, exist_ok=True)
out_exe = os.path.join(out_dir, "PlatformProfileConformanceRunner.exe")

cs_file = os.path.join(workspace, "XingShu-Dev", "tools", "PlatformProfileConformanceRunner", "Program.cs")

ref_dlls = [
    mscorlib,
    system_dll,
    system_core,
    xingshu_hooks
]

cmd = [
    dotnet_bin, csc_dll,
    "-target:exe",
    "-nostdlib",
    "-langversion:10.0",
    f"-reference:{','.join(ref_dlls)}",
    cs_file,
    f"-out:{out_exe}"
]

print("Compiling C# PlatformProfile Conformance Runner targeting .NET 4.0 runtime...")
compile_proc = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
if compile_proc.returncode != 0:
    print(f"Compilation FAILED:\nSTDOUT: {compile_proc.stdout}\nSTDERR: {compile_proc.stderr}")
    sys.exit(1)

# Copy the current runtime next to the executable.
import shutil
shutil.copy2(xingshu_hooks, os.path.join(out_dir, "XingShu.Runtime.dll"))

print("Compilation SUCCESS. Running conformance test suite...")
run_proc = subprocess.run([out_exe], capture_output=True, text=True, encoding="utf-8", errors="replace")
print(run_proc.stdout)
if run_proc.stderr:
    print("STDERR:", run_proc.stderr)

if run_proc.returncode != 0:
    print(f"Conformance tests FAILED with code {run_proc.returncode}")
    sys.exit(run_proc.returncode)

print("All C# Conformance Scenarios PASSED successfully.")
