import { describe, expect, test } from 'claude-code/testing'
import { apply, describeTool, fileName, fresh, projectName, serialize, statusLine, taskIdFrom } from './state'

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
      error: false, errorAt: 0, agents: 0, ended: false, project: '', activity: '思考中…', tasks: [],
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
    expect(line).toBe('🏠 Track - 22 · 计时 12:34 · 今日 Claude 1h05m · 注意：报错雨声不兼容')
  })

  test('spells out pauses instead of symbols of uncertain width', async () => {
    const line = statusLine({
      updatedAt: 1000, ready: true, music: 'Lo-Fi Chill Sound Track - 24', musicPlaying: false,
      timer: '30:16 ⏸ · 🍅 专注 19:23 ⏸', claudeTodaySeconds: 5040,
    }, 1000)
    expect(line).toBe('🏠 Lo-Fi Chill Sound Track - 24（暂停） · 计时 30:16（暂停） · 番茄钟 专注 19:23（暂停） · 今日 Claude 1h24m')
    expect(/[⏸⏱⚠♪🍅]/u.test(line)).toBe(false)
  })

  test('says when co-op pauses the link', async () => {
    expect(statusLine({ updatedAt: 1000, ready: true, blocked: true }, 1000)).toContain('联机中，暂停联动')
  })
})

describe('activity line', () => {
  test('names what a tool call is about to do', async () => {
    expect(describeTool('Bash', { command: 'npm test -- --watch=false' })).toBe('正在跑测试…')
    expect(describeTool('Bash', { command: 'git commit -m "fix the test"' })).toBe('正在提交代码…')
    expect(describeTool('Bash', { command: 'dotnet build game-mod -c Release' })).toBe('正在编译…')
    expect(describeTool('Bash', { command: 'ls -la' })).toBe('正在跑命令…')
    expect(describeTool('Edit', { file_path: 'C:\\repo\\src\\RoomDriver.cs' })).toBe('在改 RoomDriver.cs')
    expect(describeTool('Read', { file_path: '/home/u/README.md' })).toBe('在读 README.md')
    expect(describeTool('Grep', {})).toBe('在找代码…')
    expect(describeTool('mcp__cozy-room__room_music', {})).toBe('在布置房间…')
    expect(describeTool('ToolSearch', {})).toBe(undefined)
  })

  test('follows the turn: thinking, a tool, idle', async () => {
    const s = fresh('s1', 0)
    apply(s, { kind: 'UserPromptSubmit' }, 1)
    expect(s.activity).toBe('思考中…')
    apply(s, { kind: 'PreToolUse', activity: '正在跑测试…' }, 2)
    expect(s.activity).toBe('正在跑测试…')
    apply(s, { kind: 'PreToolUse', activity: undefined }, 3)
    expect(s.activity).toBe('正在跑测试…')
    apply(s, { kind: 'PostToolUse' }, 4)
    expect(s.activity).toBe('思考中…')
    apply(s, { kind: 'Stop' }, 5)
    expect(s.activity).toBe(null)
  })

  test('project is the folder name', async () => {
    expect(projectName('C:\\Code\\mini-cozy-room-mod')).toBe('mini-cozy-room-mod')
    expect(projectName('/home/u/app/')).toBe('app')
  })
})

describe('task list', () => {
  test('TaskCreate / TaskUpdate / TaskCompleted keep one ordered list', async () => {
    const s = fresh('s1', 0)
    apply(s, { kind: 'TaskCreated', id: '1', subject: 'Read the code' }, 1)
    apply(s, { kind: 'TaskCreated', id: '2', subject: 'Fix the bug' }, 1)
    apply(s, { kind: 'TaskCreated', id: '1', subject: 'Read the code' }, 1) // duplicate from the backup path
    apply(s, { kind: 'TaskUpdate', id: '1', status: 'in_progress' }, 2)
    apply(s, { kind: 'TaskCompleted', id: '1', subject: 'Read the code' }, 3)
    apply(s, { kind: 'TaskUpdate', id: '2', subject: 'Fix the null bug' }, 4)
    expect(s.tasks).toEqual([
      { id: '1', subject: 'Read the code', done: true },
      { id: '2', subject: 'Fix the null bug', done: false },
    ])
    apply(s, { kind: 'TaskUpdate', id: '2', status: 'deleted' }, 5)
    expect(s.tasks.length).toBe(1)
  })

  test('TodoWrite replaces the list, text is the identity', async () => {
    const s = fresh('s1', 0)
    apply(s, { kind: 'TodoWrite', todos: [{ content: 'a', status: 'completed' }, { content: 'b', status: 'pending' }] }, 1)
    expect(s.tasks).toEqual([{ id: 'todo:a', subject: 'a', done: true }, { id: 'todo:b', subject: 'b', done: false }])
  })

  test('a new session forgets the old list, a compaction keeps it', async () => {
    const s = fresh('s1', 0)
    apply(s, { kind: 'TaskCreated', id: '1', subject: 'x' }, 1)
    apply(s, { kind: 'SessionStart', source: 'compact' }, 2)
    expect(s.tasks.length).toBe(1)
    apply(s, { kind: 'SessionStart', source: 'clear' }, 3)
    expect(s.tasks.length).toBe(0)
  })

  test('task ids are found in TaskCreate results', async () => {
    expect(taskIdFrom({ task: { id: '7', subject: 'x' } })).toBe('7')
    expect(taskIdFrom('Task #12 created successfully: x')).toBe('12')
    expect(taskIdFrom(undefined)).toBe(undefined)
  })
})
