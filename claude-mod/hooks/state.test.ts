import { describe, expect, test } from 'claude-code/testing'
import { apply, celebrationFor, describeTool, displayWidth, fileName, fitLine, fresh, projectName, serialize, statusLine, statusLines, taskIdFrom } from './state'

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
      error: false, errorAt: 0, agents: 0, ended: false, project: '', activity: 'thinking', tasks: [],
      celebrate: null, celebrateAt: 0, context: null,
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
    expect(line).toBe('🏠 🎵 Track - 22 · ⏰ 12:34 · 今日 Claude 1h05m · 注意：报错雨声不兼容')
  })

  test('uses only two-cell emoji and spells out pauses', async () => {
    const line = statusLine({
      updatedAt: 1000, ready: true, music: 'Lo-Fi Chill Sound Track - 24', musicPlaying: false,
      timer: '30:16 ⏸ · 🍅 专注 19:23 ⏸', claudeTodaySeconds: 5040,
    }, 1000)
    expect(line).toBe('🏠 ⏰ 30:16 (暂停) · 🍅 专注 19:23 (暂停) · 今日 Claude 1h24m')
    expect(/[⏸⏱⚠♪]/u.test(line)).toBe(false)
  })

  test('the short form for the desktop app drops the song and the pomodoro', async () => {
    const room = {
      updatedAt: 1000, ready: true, music: 'Lo-Fi Chill Sound Track - 24', musicPlaying: false,
      timer: '35:59 ⏸ · 🍅 专注 19:23', claudeTodaySeconds: 5400,
      health: { Rain: 'incompatible: x', Music: 'incompatible: y', Work: 'ok' },
    }
    expect(statusLine(room, 1000, true)).toBe('🏠 ⏰ 35:59 (暂停) · 今日 Claude 1h30m · 注意：2 项不兼容')
    expect(statusLine(undefined, 0, true)).toBe('🏠 房间离线')
  })

  test('follows the room into English', async () => {
    const room = {
      updatedAt: 1000, ready: true, lang: 'en', music: 'Lo-Fi Chill Sound Track - 24', musicPlaying: false,
      timer: '35:59 (paused) · 🍅 Focus 19:23', claudeTodaySeconds: 5400, health: { Rain: 'incompatible: x' },
    }
    expect(statusLine(room, 1000)).toBe('🏠 ⏰ 35:59 (paused) · 🍅 Focus 19:23 · Claude today 1h30m · Note: rain on errors incompatible')
    expect(statusLine(room, 1000, true)).toBe('🏠 ⏰ 35:59 (paused) · Claude today 1h30m · Note: 1 incompatible')
    expect(statusLine({ ...room, updatedAt: 0 }, 60_000, true)).toBe('🏠 Room offline')
  })

  test('says when co-op pauses the link', async () => {
    expect(statusLine({ updatedAt: 1000, ready: true, blocked: true }, 1000)).toContain('联机中，暂停联动')
  })
})

describe('activity line', () => {
  test('names what a tool call is about to do', async () => {
    expect(describeTool('Bash', { command: 'npm test -- --watch=false' })).toBe('test')
    expect(describeTool('Bash', { command: 'git commit -m "fix the test"' })).toBe('commit')
    expect(describeTool('Bash', { command: 'dotnet build game-mod -c Release' })).toBe('build')
    expect(describeTool('Bash', { command: 'ls -la' })).toBe('command')
    expect(describeTool('Edit', { file_path: 'C:\\repo\\src\\RoomDriver.cs' })).toBe('edit:RoomDriver.cs')
    expect(describeTool('Read', { file_path: '/home/u/README.md' })).toBe('read:README.md')
    expect(describeTool('Grep', {})).toBe('search')
    expect(describeTool('mcp__cozy-room__room_music', {})).toBe('room')
    expect(describeTool('ToolSearch', {})).toBe(undefined)
  })

  test('follows the turn: thinking, a tool, idle', async () => {
    const s = fresh('s1', 0)
    apply(s, { kind: 'UserPromptSubmit' }, 1)
    expect(s.activity).toBe('thinking')
    apply(s, { kind: 'PreToolUse', activity: 'test' }, 2)
    expect(s.activity).toBe('test')
    apply(s, { kind: 'PreToolUse', activity: undefined }, 3)
    expect(s.activity).toBe('test')
    apply(s, { kind: 'PostToolUse' }, 4)
    expect(s.activity).toBe('thinking')
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

describe('fitting the hint line', () => {
  const room = {
    updatedAt: 1000, ready: true, lang: 'en', music: 'Prelude in C - Bach', musicPlaying: true, timer: '00:12',
    claudeTodaySeconds: 7800,
  }
  const hint = '⏵⏵ accept edits on (shift+tab to cycle)'

  test('versions go from everything to the essentials', async () => {
    expect(statusLines(room, 1000)).toEqual([
      '🏠 🎵 Prelude in C - Bach · ⏰ 00:12 · Claude today 2h10m',
      '🏠 🎵 Prelude in C - Bach · ⏰ 00:12',
      '🏠 🎵 Prelude in C… · ⏰ 00:12',
      '🏠 ⏰ 00:12',
      '🏠',
    ])
  })

  test('keeps the longest version that fits beside the engine text', async () => {
    const variants = statusLines(room, 1000)
    expect(fitLine(variants, hint, 200)).toBe(variants[0])
    // idle, 130 columns: everything fits (what the promo recording showed)
    expect(fitLine(variants, hint, 130)).toBe(variants[0])
    // working: the engine's pills take ~42 more columns, and "Claude today" goes first
    expect(fitLine(variants, hint, 130, true)).toBe('🏠 🎵 Prelude in C - Bach · ⏰ 00:12')
    expect(fitLine(variants, hint, 100, true)).toBe('🏠 ⏰ 00:12')
    expect(fitLine(variants, hint, undefined)).toBe(variants[0])
  })

  test('counts emoji and CJK as two cells', async () => {
    expect(displayWidth('🏠 ⏰ 00:12')).toBe(11)
    expect(displayWidth('今日 Claude')).toBe(11)
  })
})

describe('celebrations', () => {
  const bash = (command: string, stdout: string, extra: Record<string, unknown> = {}) =>
    celebrationFor('Bash', { command }, { stdout, stderr: '', interrupted: false, ...extra })

  test('a commit, from the structured git result or from git\'s summary line', async () => {
    expect(bash('git commit -m "x"', '', { gitOperation: { commit: { sha: 'abc1234', kind: 'committed' } } })).toBe('commit')
    expect(bash('git add -A && git commit -m "add tests"', '[main 8f4cd7d] add tests\n 1 file changed')).toBe('commit')
    expect(bash('git commit --amend --no-edit', '[feature/x (root-commit) 1a2b3c4] first')).toBe('commit')
    // nothing to commit exits non-zero, so it never reaches here; a push alone is no commit
    expect(bash('git push', '', { gitOperation: { push: { branch: 'main' } } })).toBe(undefined)
    expect(bash('git log -1', '[main 8f4cd7d] add tests')).toBe(undefined)
  })

  test('a merged pull request, not an armed auto-merge', async () => {
    expect(bash('gh pr merge 12 --squash', '', { gitOperation: { pr: { number: 12, action: 'merged' } } })).toBe('merge')
    expect(bash('gh pr merge 12 --squash', '✓ Squashed and merged pull request #12 (Fix it)\n✓ Merged pull request #12')).toBe('merge')
    expect(bash('gh pr merge 12 --auto', '', { gitOperation: { pr: { number: 12, action: 'auto-merge-enabled' } } })).toBe(undefined)
    expect(bash('gh pr merge 12 --auto', '✓ Pull request #12 will be automatically merged via auto-merge')).toBe(undefined)
    // what the Bash tool really gets: gh prints nothing when stdout isn't a terminal
    expect(bash('gh pr merge 1 --merge', '')).toBe('merge')
    expect(bash('gh pr merge 1 --merge --delete-branch && git pull', 'Updating 3c455f2..bc5fe44\nFast-forward')).toBe('merge')
    expect(bash('gh pr merge 12 --auto --squash', '')).toBe(undefined)
    expect(bash('gh pr merge 12 --disable-auto', '')).toBe(undefined)
    expect(bash('gh pr merge --help', 'Merge a pull request on GitHub.')).toBe(undefined)
    expect(bash('gh pr merge 12 || true', 'X Pull request #12 is not mergeable: the base branch policy prohibits the merge.')).toBe(undefined)
    expect(bash('gh pr merge 12 || true', 'GraphQL: Could not resolve to a PullRequest (repository.pullRequest)')).toBe(undefined)
    expect(celebrationFor('mcp__github__merge_pull_request', {}, '{"merged":true,"sha":"abc"}')).toBe('merge')
    expect(celebrationFor('mcp__github__merge_pull_request', {}, '{"merged":false,"message":"not mergeable"}')).toBe(undefined)
  })

  test('a passing test run, in the words of different runners', async () => {
    expect(bash('npx vitest run', ' Test Files  3 passed (3)\n      Tests  26 passed (26)')).toBe('tests')
    expect(bash('pytest -q', '........ 8 passed in 0.12s')).toBe('tests')
    expect(bash('dotnet test', 'Passed!  - Failed:     0, Passed:    26, Skipped:     0, Total:    26')).toBe('tests')
    expect(bash('cargo test', 'test result: ok. 4 passed; 0 failed; 0 ignored')).toBe('tests')
    expect(bash('go test ./...', 'ok  \texample.com/pkg\t0.01s')).toBe('tests')
    expect(bash('node --test', 'ℹ tests 5\nℹ pass 5\nℹ fail 0')).toBe('tests')
    expect(bash('npx mocha', '  26 passing (40ms)')).toBe('tests')
    expect(bash('claude plugin test claude-mod', '(pass) a > b [0.2ms]\n\n 34 pass\n 0 fail\nRan 34 tests across 2 files.')).toBe('tests')
  })

  test('no party for failures, piped-away exit codes, or things that only look like tests', async () => {
    expect(bash('npm test | tail -5', 'Tests  1 failed | 25 passed (26)')).toBe(undefined)
    expect(bash('pytest | tail -1', '==== 1 failed, 7 passed in 0.3s ====')).toBe(undefined)
    expect(bash('dotnet test', 'Failed!  - Failed:     1, Passed:    25')).toBe(undefined)
    expect(bash('go test ./...', '--- FAIL: TestX (0.00s)\nFAIL\nok  \tother\t0.01s')).toBe(undefined)
    expect(bash('node --test', 'ℹ pass 4\nℹ fail 1')).toBe(undefined)
    expect(bash('claude plugin test claude-mod | tail -3', ' 34 pass\n 1 fail\nRan 35 tests across 2 files.')).toBe(undefined)
    expect(bash('ls tests', 'a.test.ts\nb.test.ts')).toBe(undefined)
    expect(bash('npm test', '5 passed', { backgroundTaskId: 'b1' })).toBe(undefined)
    expect(bash('npm test', '5 passed', { interrupted: true })).toBe(undefined)
    expect(celebrationFor('Read', { file_path: 'x' }, '5 passed')).toBe(undefined)
  })

  test('the session remembers the latest one with its time, and the file carries it', async () => {
    const s = fresh('s1', 0)
    apply(s, { kind: 'Celebrate', what: 'commit' }, 5)
    apply(s, { kind: 'Celebrate', what: 'tests' }, 9)
    const json = JSON.parse(serialize(s, 'tok', 10))
    expect(json.celebrate).toBe('tests')
    expect(json.celebrateAt).toBe(9)
  })
})

describe('context fill', () => {
  test('is kept as a whole percentage and forgotten when the conversation starts over', async () => {
    const s = fresh('s1', 0)
    expect(JSON.parse(serialize(s, 'tok', 1)).context).toBe(null)
    apply(s, { kind: 'Context', percent: 83.6 }, 1)
    expect(s.context).toBe(84)
    apply(s, { kind: 'Context', percent: 130 }, 2)
    expect(s.context).toBe(100)
    apply(s, { kind: 'SessionStart', source: 'compact' }, 3)
    expect(s.context).toBe(null)
    apply(s, { kind: 'Context', percent: 12 }, 4)
    apply(s, { kind: 'Context' }, 5)
    expect(s.context).toBe(null)
  })
})
