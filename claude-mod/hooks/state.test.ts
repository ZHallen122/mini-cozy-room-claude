import { describe, expect, test } from 'claude-code/testing'
import { apply, fileName, fresh, serialize, statusLine } from './state'

describe('session state', () => {
  test('a prompt starts work and Stop ends it', async () => {
    const s = fresh('s1', 0)
    apply(s, { kind: 'SessionStart', source: 'startup' }, 1)
    expect(s.working).toBe(false)
    apply(s, { kind: 'UserPromptSubmit' }, 2)
    expect(s.working).toBe(true)
    apply(s, { kind: 'Stop' }, 3)
    expect(s.working).toBe(false)
  })

  test('a permission request waits until that agent moves on', async () => {
    const s = fresh('s1', 0)
    apply(s, { kind: 'UserPromptSubmit' }, 1)
    apply(s, { kind: 'PermissionRequest' }, 2)
    apply(s, { kind: 'PermissionRequest', agent: 'a1' }, 2)
    expect(s.waiting.size).toBe(2)
    // approved: the main thread's tool ran
    apply(s, { kind: 'PostToolUse' }, 3)
    expect(s.waiting.size).toBe(1)
    // the subagent was denied and stopped
    apply(s, { kind: 'SubagentStop', agent: 'a1' }, 4)
    expect(s.waiting.size).toBe(0)
  })

  test('a denied prompt clears on the next event of the turn', async () => {
    const s = fresh('s1', 0)
    apply(s, { kind: 'PermissionRequest' }, 1)
    apply(s, { kind: 'Stop' }, 2)
    expect(s.waiting.size).toBe(0)
  })

  test('a failing tool raises the error until a tool succeeds', async () => {
    const s = fresh('s1', 0)
    apply(s, { kind: 'PostToolUseFailure' }, 5)
    expect(s.error).toBe(true)
    expect(s.errorAt).toBe(5)
    apply(s, { kind: 'PostToolUse' }, 6)
    expect(s.error).toBe(false)
  })

  test('an interrupted tool is not an error', async () => {
    const s = fresh('s1', 0)
    apply(s, { kind: 'PostToolUseFailure', interrupted: true }, 5)
    expect(s.error).toBe(false)
  })

  test('subagents are counted and cleared when the session ends', async () => {
    const s = fresh('s1', 0)
    apply(s, { kind: 'SubagentStart', agent: 'a1' }, 1)
    apply(s, { kind: 'SubagentStart', agent: 'a2' }, 1)
    expect(s.agents.size).toBe(2)
    apply(s, { kind: 'SessionEnd' }, 2)
    expect(s.agents.size).toBe(0)
    expect(s.ended).toBe(true)
  })

  test('the file carries the token and the heartbeat time', async () => {
    const s = fresh('s1', 0)
    apply(s, { kind: 'UserPromptSubmit' }, 1)
    const json = JSON.parse(serialize(s, 'tok', 99))
    expect(json).toEqual({
      v: 1, tok: 'tok', sid: 's1', startedAt: 0, updatedAt: 99, eventAt: 1, working: true, waiting: 0,
      error: false, errorAt: 0, agents: 0, ended: false,
    })
  })

  test('session ids become safe file names', async () => {
    expect(fileName('e7b67d8e-b02e-4b68-9e5d-4d2c257aeb90')).toBe('e7b67d8e-b02e-4b68-9e5d-4d2c257aeb90.json')
    expect(fileName('..\\evil/x')).toBe('___evil_x.json')
  })
})

describe('status line', () => {
  test('a stale or missing room.json says the room is offline', async () => {
    expect(statusLine(undefined, 0)).toContain('房间离线')
    expect(statusLine({ updatedAt: 0 }, 60_000)).toContain('房间离线')
  })

  test('shows music, timer, today and broken features', async () => {
    const line = statusLine({
      updatedAt: 1000, ready: true, blocked: false, music: 'Track - 22', musicPlaying: true, timer: '12:34',
      claudeTodaySeconds: 3900, health: { Work: 'ok', Rain: 'incompatible: MissingMethodException: x' },
    }, 2000)
    expect(line).toBe('🏠 · ♪ Track - 22 · ⏱ 12:34 · 今日 Claude 1h05m · ⚠ 报错雨声不兼容')
  })

  test('says when co-op pauses the link', async () => {
    expect(statusLine({ updatedAt: 1000, ready: true, blocked: true }, 1000)).toContain('联机中·暂停联动')
  })
})
