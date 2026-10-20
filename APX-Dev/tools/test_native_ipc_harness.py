import argparse
import os
import sys
import time
import struct
import tempfile
import subprocess
import shutil

COMMAND_MAGIC = b"APXS"
STATUS_MAGIC = b"APXR"
IPC_VERSION = 1

FLAG_ENABLED = 0x01
FLAG_RESET = 0x02
FLAG_SHUTDOWN = 0x04

COMMAND_SIZE = 24
STATUS_SIZE = 40

def pack_command(sequence, flags, speed):
    return struct.pack("<4sIQIf", COMMAND_MAGIC, IPC_VERSION, sequence, flags, speed)

def parse_status(data):
    if len(data) < STATUS_SIZE:
        raise ValueError(f"Status data too short: {len(data)}")
    magic, ver, ack_seq, flags, cfg_speed, eff_speed, err_code, caps, managed = struct.unpack(
        "<4sIQIffiii", data[:STATUS_SIZE]
    )
    return {
        "magic": magic,
        "version": ver,
        "ack_sequence": ack_seq,
        "flags": flags,
        "configured_speed": cfg_speed,
        "effective_speed": eff_speed,
        "error_code": err_code,
        "capabilities": caps,
        "managed_mode": managed
    }

CHILD_RUNNER_CODE = """
import sys, time, ctypes, os
from datetime import datetime, timezone
from ctypes import wintypes

kernel32 = ctypes.WinDLL('kernel32')
kernel32.QueryPerformanceCounter.argtypes = [ctypes.POINTER(ctypes.c_int64)]
kernel32.QueryPerformanceFrequency.argtypes = [ctypes.POINTER(ctypes.c_int64)]
kernel32.GetTickCount64.restype = wintypes.ULARGE_INTEGER
kernel32.QueryUnbiasedInterruptTime.argtypes = [ctypes.POINTER(ctypes.c_uint64)]
kernel32.QueryUnbiasedInterruptTime.restype = wintypes.BOOL

print(f"CHILD_PID:{os.getpid()}", flush=True)

# Wait for signal before loading DLL to allow parent to plant stale files
cmd = sys.stdin.readline().strip()
if cmd != "LOAD_DLL":
    sys.exit(1)

dll_path = sys.argv[1]
h = ctypes.WinDLL(dll_path)
print(f"CHILD_READY:{os.getpid()}", flush=True)

# Keep process alive and respond to timing requests
while True:
    line = sys.stdin.readline()
    if not line:
        break
    cmd = line.strip()
    if cmd == "EXIT":
        break
    elif cmd == "MEASURE_UTC":
        start = datetime.now(timezone.utc)
        time.sleep(0.15)
        elapsed = (datetime.now(timezone.utc) - start).total_seconds()
        print(f"MEASURE_RESULT:{elapsed:.4f}", flush=True)
    elif cmd == "MEASURE_REAL":
        start, end = ctypes.c_uint64(), ctypes.c_uint64()
        assert kernel32.QueryUnbiasedInterruptTime(ctypes.byref(start))
        time.sleep(0.15)
        assert kernel32.QueryUnbiasedInterruptTime(ctypes.byref(end))
        print(f"MEASURE_RESULT:{(end.value - start.value) / 10000000:.4f}", flush=True)
    elif cmd == "MEASURE_QPC":
        # Measure elapsed QPC ticks over real 100ms
        freq = ctypes.c_int64()
        kernel32.QueryPerformanceFrequency(ctypes.byref(freq))
        t1 = ctypes.c_int64()
        kernel32.QueryPerformanceCounter(ctypes.byref(t1))
        time.sleep(0.15)
        t2 = ctypes.c_int64()
        kernel32.QueryPerformanceCounter(ctypes.byref(t2))
        elapsed_sec = (t2.value - t1.value) / freq.value
        print(f"MEASURE_RESULT:{elapsed_sec:.4f}", flush=True)
"""

def main():
    print("==================================================")
    print("APX Phase 2D-2B3-R2 Native IPC Control Test Harness")
    print("==================================================")

    base_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    parser = argparse.ArgumentParser(description="Test native IPC and actual clock dilation in an isolated child process")
    parser.add_argument("--dll", default=os.path.join(base_dir, "external", "native-speed-lab", "adapter", "target", "release", "version.dll"))
    built_dll = os.path.abspath(parser.parse_args().dll)

    if not os.path.isfile(built_dll):
        raise FileNotFoundError(f"Built DLL not found: {built_dll}")

    print(f"Target DLL: {built_dll}")

    # Launch child process
    runner_py = os.path.join(base_dir, "scratch", "ipc_child_runner.py")
    os.makedirs(os.path.dirname(runner_py), exist_ok=True)
    with open(runner_py, "w", encoding="utf-8") as f:
        f.write("import os\n" + CHILD_RUNNER_CODE)

    proc = subprocess.Popen(
        [sys.executable, runner_py, built_dll],
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True
    )

    pid_line = proc.stdout.readline().strip()
    assert pid_line.startswith("CHILD_PID:"), f"Expected CHILD_PID, got {pid_line}"
    child_pid = int(pid_line.split(":")[1])
    print(f"Child process launched with PID: {child_pid}")

    session_dir = os.path.join(tempfile.gettempdir(), "APXNativeSpeed", str(child_pid))
    status_file = os.path.join(session_dir, "status.bin")
    command_file = os.path.join(session_dir, "command.bin")
    command_tmp = os.path.join(session_dir, "command.tmp")

    print(f"Session Dir: {session_dir}")

    # Pre-Load: Plant stale session files before DLL load (PID reuse simulation)
    print("\n--- Pre-Load: Stale Session Reuse Setup (PID Reuse Simulation) ---")
    os.makedirs(session_dir, exist_ok=True)
    stale_cmd = pack_command(9999, FLAG_ENABLED, 5.0)
    with open(command_file, "wb") as f:
        f.write(stale_cmd)

    stale_status = struct.pack("<4sIQIffiii", STATUS_MAGIC, IPC_VERSION, 9999, FLAG_ENABLED, 5.0, 5.0, 0, 0x0F, 1)
    with open(status_file, "wb") as f:
        f.write(stale_status)

    print(f"Planted stale command.bin (Seq=9999, Flags=ENABLED, Speed=5.0, Size={os.path.getsize(command_file)})")
    print(f"Planted stale status.bin  (Ack=9999, Flags=ENABLED, Eff=5.0, Size={os.path.getsize(status_file)})")
    assert os.path.isfile(command_file) and os.path.isfile(status_file)

    # Signal child to load DLL
    proc.stdin.write("LOAD_DLL\n")
    proc.stdin.flush()

    ready_line = proc.stdout.readline().strip()
    print(f"Child handshake after DLL load: {ready_line}")

    # Step 1: Wait for status.bin and verify stale session purge
    print("\n--- Test 1: Stale Session Purge & Startup Status Check ---")
    status = None
    for _ in range(50):
        if os.path.isfile(status_file) and os.path.getsize(status_file) >= STATUS_SIZE:
            try:
                with open(status_file, "rb") as f:
                    data = f.read()
                st = parse_status(data)
                if st["ack_sequence"] == 0:
                    status = st
                    break
            except Exception:
                pass
        time.sleep(0.05)

    if not status:
        proc.kill()
        raise RuntimeError("Failed to read fresh initial status.bin (stale status was not purged)")

    print(f"Fresh Initial Status:")
    print(f"  Magic:           {status['magic']}")
    print(f"  Version:         {status['version']}")
    print(f"  AckSequence:     {status['ack_sequence']}")
    print(f"  Flags:           0x{status['flags']:02X}")
    print(f"  ConfiguredSpeed: {status['configured_speed']}")
    print(f"  EffectiveSpeed:  {status['effective_speed']}")
    print(f"  Capabilities:    0x{status['capabilities']:02X}")
    print(f"  ManagedMode:     {status['managed_mode']}")

    assert status["magic"] == STATUS_MAGIC, "Status magic mismatch"
    assert status["version"] == 1, "Status version mismatch"
    assert status["ack_sequence"] == 0, f"Stale AckSequence must be reset to 0, got {status['ack_sequence']}"
    assert status["effective_speed"] == 1.0, f"Initial effective speed must be 1.0, got {status['effective_speed']}"
    assert status["flags"] == 0, f"Initial flags must be 0 (Disabled), got 0x{status['flags']:02X}"
    assert status["managed_mode"] == 1, "Managed mode must be 1"
    assert not os.path.isfile(command_file), "Stale command.bin must be purged upon worker startup!"
    print("-> Test 1 PASS (Stale command.bin purged, stale status.bin replaced, initial state is baseline 1.0x/Disabled)")

    def send_cmd(seq, flags, speed):
        buf = pack_command(seq, flags, speed)
        with open(command_tmp, "wb") as f:
            f.write(buf)
        os.replace(command_tmp, command_file)

    def wait_ack(expected_seq, timeout=2.0):
        t0 = time.time()
        while time.time() - t0 < timeout:
            if os.path.isfile(status_file):
                try:
                    with open(status_file, "rb") as f:
                        st = parse_status(f.read())
                    if st["ack_sequence"] == expected_seq:
                        return st
                except Exception:
                    pass
            time.sleep(0.02)
        raise TimeoutError(f"Timeout waiting for AckSequence {expected_seq}")

    def measure_child_qpc(command="MEASURE_QPC"):
        proc.stdin.write(command + "\n")
        proc.stdin.flush()
        while True:
            line = proc.stdout.readline()
            if not line:
                raise RuntimeError("EOF while waiting for MEASURE_RESULT")
            line = line.strip()
            if line.startswith("MEASURE_RESULT:"):
                return float(line.split(":")[1])

    # Baseline 1.0x timing
    print("\n--- Test 2: Baseline 1.0x Timing (Stale Auto-Speed Check) ---")
    t_base = measure_child_qpc()
    print(f"Baseline QPC elapsed in ~150ms real time: {t_base:.4f}s")
    assert 0.12 <= t_base <= 0.25, f"Baseline QPC out of bounds: {t_base}"
    print("STALE SESSION AUTO-SPEED: NO (Verified ~1.0x baseline timing, NOT 5.0x)")
    print("-> Test 2 PASS")

    # Step 3: Set 2.0x Enabled
    print("\n--- Test 3: 2.0x Enabled Command ---")
    send_cmd(1, FLAG_ENABLED, 2.0)
    st1 = wait_ack(1)
    print(f"Ack 1: EffectiveSpeed={st1['effective_speed']}, Flags=0x{st1['flags']:02X}")
    assert abs(st1["effective_speed"] - 2.0) < 0.01, f"Effective speed should be 2.0, got {st1['effective_speed']}"
    assert st1["flags"] & FLAG_ENABLED != 0, "Enabled flag should be set"
    t_2x = measure_child_qpc()
    print(f"2.0x QPC elapsed in ~150ms real time: {t_2x:.4f}s (Ratio: {t_2x/t_base:.2f}x)")
    assert 1.7 <= (t_2x / t_base) <= 2.3, f"Time dilation ratio not ~2.0x: {t_2x / t_base}"
    print("-> Test 3 PASS")

    # Step 4: Set 0.5x Enabled
    print("\n--- Test 4: 0.5x Enabled Command ---")
    send_cmd(2, FLAG_ENABLED, 0.5)
    st2 = wait_ack(2)
    print(f"Ack 2: EffectiveSpeed={st2['effective_speed']}, Flags=0x{st2['flags']:02X}")
    assert abs(st2["effective_speed"] - 0.5) < 0.01, f"Effective speed should be 0.5, got {st2['effective_speed']}"
    t_half = measure_child_qpc()
    print(f"0.5x QPC elapsed in ~150ms real time: {t_half:.4f}s (Ratio: {t_half/t_base:.2f}x)")
    assert 0.4 <= (t_half / t_base) <= 0.65, f"Time dilation ratio not ~0.5x: {t_half / t_base}"
    print("-> Test 4 PASS")

    # Step 5: Disable (Keep configured 0.5x)
    print("\n--- Test 5: Disable Command ---")
    send_cmd(3, 0, 0.5)
    st3 = wait_ack(3)
    print(f"Ack 3: EffectiveSpeed={st3['effective_speed']}, ConfiguredSpeed={st3['configured_speed']}, Flags=0x{st3['flags']:02X}")
    assert abs(st3["effective_speed"] - 1.0) < 0.01, f"Disabled effective speed must be 1.0, got {st3['effective_speed']}"
    assert abs(st3["configured_speed"] - 0.5) < 0.01, f"Configured speed should be preserved as 0.5"
    assert st3["flags"] & FLAG_ENABLED == 0, "Enabled flag should be cleared"
    t_dis = measure_child_qpc()
    print(f"Disabled QPC elapsed: {t_dis:.4f}s (Ratio: {t_dis/t_base:.2f}x)")
    assert 0.85 <= (t_dis / t_base) <= 1.25, f"Disabled timing not at baseline: {t_dis / t_base}"
    print("-> Test 5 PASS")

    # Step 6: Reset Command
    print("\n--- Test 6: Reset Command ---")
    send_cmd(4, FLAG_RESET, 1.0)
    st4 = wait_ack(4)
    print(f"Ack 4: EffectiveSpeed={st4['effective_speed']}, Flags=0x{st4['flags']:02X}")
    assert abs(st4["effective_speed"] - 1.0) < 0.01, f"Reset effective speed must be 1.0, got {st4['effective_speed']}"
    assert st4["flags"] & FLAG_ENABLED == 0, "Reset flags must have Enabled=0"
    print("-> Test 6 PASS")

    # Step 7: Malformed Command Robustness
    print("\n--- Test 7: Malformed Commands Safety ---")
    # Sub-test 7A: Truncated command
    with open(command_tmp, "wb") as f:
        f.write(b"APXS\x01\x00") # only 6 bytes
    os.replace(command_tmp, command_file)
    time.sleep(0.1)
    with open(status_file, "rb") as f:
        st_err1 = parse_status(f.read())
    print(f"Truncated command error code: {st_err1['error_code']}")
    assert st_err1["error_code"] != 0, "Should report non-zero error code"
    assert st_err1["effective_speed"] == 1.0, "Effective speed must remain 1.0"

    # Sub-test 7B: Bad magic
    bad_magic_cmd = struct.pack("<4sIQIf", b"XXXX", IPC_VERSION, 5, FLAG_ENABLED, 2.0)
    with open(command_tmp, "wb") as f:
        f.write(bad_magic_cmd)
    os.replace(command_tmp, command_file)
    time.sleep(0.1)
    with open(status_file, "rb") as f:
        st_err2 = parse_status(f.read())
    print(f"Bad magic command error code: {st_err2['error_code']}")
    assert st_err2["error_code"] != 0, "Should report non-zero error code"

    # Sub-test 7C: Old sequence
    old_seq_cmd = struct.pack("<4sIQIf", COMMAND_MAGIC, IPC_VERSION, 2, FLAG_ENABLED, 3.0)
    with open(command_tmp, "wb") as f:
        f.write(old_seq_cmd)
    os.replace(command_tmp, command_file)
    time.sleep(0.1)
    with open(status_file, "rb") as f:
        st_err3 = parse_status(f.read())
    assert st_err3["ack_sequence"] == 4, f"Old sequence should be ignored, expected Ack 4, got {st_err3['ack_sequence']}"
    print("-> Test 7 PASS (No crashes, errors reported safely)")

    # The upper limit must reach the native timer hooks, not only the UI slider.
    print("\n--- Test 8: 10.0x Timing and Range Validation ---")
    send_cmd(5, FLAG_ENABLED, 10.0)
    st_max = wait_ack(5)
    assert st_max["error_code"] == 0 and st_max["effective_speed"] == 10.0, st_max
    t_10x = measure_child_qpc()
    ratio = t_10x / t_base
    print(f"10.0x QPC elapsed in ~150ms real time: {t_10x:.4f}s (Ratio: {ratio:.2f}x)")
    assert 8.0 <= ratio <= 12.0, f"Time dilation ratio not ~10.0x: {ratio}"
    real_at_10x = measure_child_qpc("MEASURE_REAL")
    assert .12 <= real_at_10x <= .25, f"Decision clock accelerated at 10x: {real_at_10x}"
    print(f"Decision real clock at 10x: {real_at_10x:.4f}s over 150ms (unaccelerated)")
    utc_at_10x = measure_child_qpc("MEASURE_UTC")
    assert .12 <= utc_at_10x <= .25, f"UTC fallback accelerated at 10x: {utc_at_10x}"
    print(f"UTC fallback clock at 10x: {utc_at_10x:.4f}s over 150ms (unaccelerated)")
    for seq, speed in enumerate((10.01, float("nan"), float("inf"), 0.249), start=6):
        send_cmd(seq, 0, speed)
        st_invalid = wait_ack(seq)
        assert st_invalid["error_code"] == -4, st_invalid
        assert st_invalid["configured_speed"] == 10.0 and st_invalid["effective_speed"] == 1.0, st_invalid
    print("-> Test 8 PASS (10x effective; invalid values rejected without changing configured speed)")

    # Step 9: Clean shutdown
    print("\n--- Test 9: Clean Shutdown ---")
    send_cmd(10, FLAG_SHUTDOWN, 1.0)
    st_exit = wait_ack(10)
    print(f"Ack 10: EffectiveSpeed={st_exit['effective_speed']}")
    proc.stdin.write("EXIT\n")
    proc.stdin.flush()
    proc.wait(timeout=2.0)
    print(f"Child process exited with code: {proc.returncode}")
    assert proc.returncode == 0, f"Child exited with non-zero code {proc.returncode}"
    print("-> Test 9 PASS")

    print("\n==================================================")
    print("ALL NATIVE IPC HARNESS TESTS PASSED SUCCESSFULLY!")
    print("==================================================")

if __name__ == "__main__":
    main()
