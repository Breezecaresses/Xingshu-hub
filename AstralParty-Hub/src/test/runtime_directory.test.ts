import assert from 'node:assert/strict';
import { selectedRuntimeSessions } from '../pages/runtime/selectedRuntimeSessions.ts';

const session = { platformId: 'STEAM_CN', executablePath: 'E:\\Steam\\8vJXn6CN\\AstralParty_CN.exe', connectionState: 'CONNECTED' };
assert.equal(selectedRuntimeSessions([session], 'STEAM_CN', 'e:/steam/8vjxn6cn/astralparty_cn.exe').length, 1);
assert.equal(selectedRuntimeSessions([session], 'STEAM_CN', 'D:/other/AstralParty_CN.exe').length, 0);
assert.equal(selectedRuntimeSessions([session], 'TAPTAP', session.executablePath).length, 0);
assert.equal(selectedRuntimeSessions([{ ...session, connectionState: 'DISCONNECTED' }], 'STEAM_CN', session.executablePath).length, 0);
assert.equal(selectedRuntimeSessions([{ ...session, executablePath: null }], 'STEAM_CN', session.executablePath).length, 0);
assert.equal(selectedRuntimeSessions([session], 'STEAM_CN', null).length, 0);
console.log('Runtime directory isolation tests passed.');
