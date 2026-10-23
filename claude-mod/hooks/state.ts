// Pure session-state logic, kept apart from the engine calls so it can be tested on its own.

export type Task = { id: string; subject: string; done: boolean }

export type SessionState = {
  sid: string
  startedAt: number
  eventAt: number
  working: boolean
  // agent ids ("main" for the main thread) waiting on a permission prompt
  waiting: Set<string>
  error: boolean
  errorAt: number
  // running subagents
  agents: Set<string>
  ended: boolean
  // folder name of the session's working directory, for the room's task list title
  project: string
  // one line on what Claude is doing right now, shown above the character; null when idle
  activity: string | null
  // Claude's own task list (TaskCreate/TaskUpdate or TodoWrite), in creation order
  tasks: Task[]
}

export type CozyEvent =
  | { kind: 'SessionStart'; source?: string }
  | { kind: 'UserPromptSubmit' }
  | { kind: 'PreToolUse'; agent?: string; activity?: string | null }
  | { kind: 'TaskCreated'; id: string; subject: string }
  | { kind: 'TaskCompleted'; id: string; subject: string }
  | { kind: 'TaskUpdate'; id: string; status?: string; subject?: string }
  | { kind: 'TodoWrite'; todos: { content: string; status: string }[] }
  | { kind: 'PermissionRequest'; agent?: string }
  | { kind: 'Notification'; agent?: string; type?: string }
  | { kind: 'PostToolUse'; agent?: string }
  | { kind: 'PostToolUseFailure'; agent?: string; interrupted?: boolean }
  | { kind: 'Stop' }
  | { kind: 'StopFailure' }
  | { kind: 'SubagentStart'; agent?: string }
  | { kind: 'SubagentStop'; agent?: string }
  | { kind: 'SessionEnd' }

export function fresh(sid: string, now: number): SessionState {
  return {
    sid, startedAt: now, eventAt: now, working: false, waiting: new Set(), error: false, errorAt: 0,
    agents: new Set(), ended: false, project: '', activity: null, tasks: [],
  }
}

const MAX_TASKS = 30
const MAX_SUBJECT = 80
// what the bubble shows between tools; activity values are codes the game mod words in the room's language
const THINKING = 'thinking'

function clip(text: string, max: number): string {
  const t = text.replace(/\s+/g, ' ').trim()
  return t.length > max ? t.slice(0, max - 1) + '…' : t
}

function upsertTask(s: SessionState, id: string, subject: string | undefined, done: boolean | undefined): void {
  let task = s.tasks.find(t => t.id === id)
  if (!task) {
    if (s.tasks.length >= MAX_TASKS || !subject) return
    task = { id, subject: clip(subject, MAX_SUBJECT), done: false }
    s.tasks.push(task)
  }
  if (subject) task.subject = clip(subject, MAX_SUBJECT)
  if (done !== undefined) task.done = done
}

export function apply(s: SessionState, ev: CozyEvent, now: number): void {
  s.eventAt = now
  const who = 'agent' in ev && ev.agent ? ev.agent : 'main'
  switch (ev.kind) {
    case 'SessionStart':
      s.ended = false; s.working = false; s.waiting.clear(); s.error = false; s.activity = null
      if (ev.source !== 'compact') { s.agents.clear(); s.tasks = [] }
      break
    case 'UserPromptSubmit':
      s.working = true; s.waiting.clear(); s.error = false; s.activity = THINKING
      break
    case 'PreToolUse':
      s.working = true; s.waiting.delete(who)
      if (ev.activity !== undefined) s.activity = ev.activity
      break
    case 'TaskCreated':
      upsertTask(s, ev.id, ev.subject, false)
      break
    case 'TaskCompleted':
      upsertTask(s, ev.id, ev.subject, true)
      break
    case 'TaskUpdate':
      if (ev.status === 'deleted') s.tasks = s.tasks.filter(t => t.id !== ev.id)
      else upsertTask(s, ev.id, ev.subject, ev.status === undefined ? undefined : ev.status === 'completed')
      break
    case 'TodoWrite':
      // TodoWrite always sends the whole list; its items have no ids, so the text is the identity
      s.tasks = ev.todos.filter(t => t.content).slice(0, MAX_TASKS)
        .map(t => ({ id: 'todo:' + clip(t.content, MAX_SUBJECT), subject: clip(t.content, MAX_SUBJECT), done: t.status === 'completed' }))
      break
    case 'PermissionRequest':
      s.working = true; s.waiting.add(who)
      break
    case 'Notification':
      if (ev.type === 'permission_prompt' || ev.type === 'elicitation_dialog') s.waiting.add(who)
      break
    case 'PostToolUse':
      // a tool that succeeds is the "back to normal" signal for the rain
      s.working = true; s.waiting.delete(who); s.error = false; s.activity = THINKING
      break
    case 'PostToolUseFailure':
      s.working = true; s.waiting.delete(who); s.activity = THINKING
      if (!ev.interrupted) { s.error = true; s.errorAt = now }
      break
    case 'Stop':
      s.working = false; s.waiting.clear(); s.error = false; s.activity = null
      break
    case 'StopFailure':
      s.working = false; s.waiting.clear(); s.error = true; s.errorAt = now; s.activity = null
      break
    case 'SubagentStart':
      if (ev.agent) s.agents.add(ev.agent)
      break
    case 'SubagentStop':
      if (ev.agent) { s.agents.delete(ev.agent); s.waiting.delete(ev.agent) }
      break
    case 'SessionEnd':
      s.ended = true; s.working = false; s.waiting.clear(); s.agents.clear(); s.activity = null
      break
  }
}

function base(path: unknown): string {
  return typeof path === 'string' ? path.split(/[\\/]/).filter(Boolean).pop() ?? '' : ''
}

export function projectName(cwd: unknown): string {
  return clip(base(cwd), 24)
}

// TaskCreate's result names the new task's id; its exact shape isn't documented, so look for it loosely.
export function taskIdFrom(response: unknown): string | undefined {
  if (response && typeof response === 'object') {
    const r = response as Record<string, unknown>
    const task = (r.task && typeof r.task === 'object' ? r.task : r) as Record<string, unknown>
    for (const k of ['id', 'taskId', 'task_id'])
      if (typeof task[k] === 'string' || typeof task[k] === 'number') return String(task[k])
  }
  const text = typeof response === 'string' ? response : JSON.stringify(response ?? '')
  return /"(?:id|taskId|task_id)"\s*:\s*"?(\w+)"?/.exec(text)?.[1] ?? /#(\d+)/.exec(text)?.[1]
}

const TEST = /\b(test|tests|pytest|jest|vitest|mocha|cargo test|go test|dotnet test|npm (run )?test|pnpm test|yarn test)\b/
const BUILD = /\b(build|tsc|compile|make|cmake|msbuild|webpack|vite build|gradle|mvn)\b/

// What a tool call is about to do, as a code for the bubble ("test", "edit:README.md"); the game mod words it in
// the room's language. Undefined keeps the current one (for calls that say nothing useful on their own).
export function describeTool(tool: string, input: Record<string, unknown>): string | null | undefined {
  switch (tool) {
    case 'Bash':
    case 'PowerShell': {
      const cmd = typeof input.command === 'string' ? input.command : ''
      // git first: a commit message may well mention "test" or "build"
      if (/\bgit\s+commit\b/.test(cmd)) return 'commit'
      if (/\bgit\s+(push|pull|fetch|clone)\b/.test(cmd)) return 'sync'
      if (TEST.test(cmd)) return 'test'
      if (/\b(npm|pnpm|yarn)\s+(i|install|add)\b|\bpip\s+install\b|\bdotnet\s+(restore|add)\b/.test(cmd)) return 'install'
      if (BUILD.test(cmd)) return 'build'
      return 'command'
    }
    case 'Edit':
    case 'Write':
    case 'MultiEdit':
    case 'NotebookEdit': {
      const name = base(input.file_path ?? input.notebook_path)
      return name ? `edit:${clip(name, 28)}` : 'edit'
    }
    case 'Read': {
      const name = base(input.file_path)
      return name ? `read:${clip(name, 28)}` : 'read'
    }
    case 'Grep':
    case 'Glob':
      return 'search'
    case 'WebFetch':
    case 'WebSearch':
      return 'web'
    case 'Agent':
    case 'Task':
      return 'agent'
    case 'TaskCreate':
    case 'TaskUpdate':
    case 'TodoWrite':
      return 'plan'
    case 'ToolSearch':
    case 'TaskList':
    case 'TaskGet':
      return undefined
    default:
      if (tool.startsWith('mcp__cozy-room__')) return 'room'
      if (tool.startsWith('mcp__')) return `tool:${clip(tool.split('__')[1] ?? tool, 20)}`
      return `tool:${clip(tool, 20)}`
  }
}

// The file the game mod reads. `updatedAt` doubles as a heartbeat: a session that stops writing is gone.
export function serialize(s: SessionState, token: string, now: number): string {
  return JSON.stringify({
    v: 1, tok: token, sid: s.sid, startedAt: s.startedAt, updatedAt: now, eventAt: s.eventAt,
    working: s.working, waiting: s.waiting.size, error: s.error, errorAt: s.errorAt,
    agents: s.agents.size, ended: s.ended, project: s.project, activity: s.activity, tasks: s.tasks,
  })
}

// session ids are UUIDs; anything else is reduced to a safe file name
export function fileName(sid: string): string {
  return sid.replace(/[^A-Za-z0-9_-]/g, '_').slice(0, 80) + '.json'
}

const FEATURES_EN: Record<string, string> = {
  Guard: 'co-op guard', Work: 'work sync', Waiting: 'waiting reminder', Timer: 'timer link',
  Restore: 'restore on session end', Rain: 'rain on errors', Status: 'room status line', Subagents: 'subagent pets',
  Notes: "Claude's memos/todos", Pomodoro: "Claude's pomodoro", Music: "Claude's music", Ambience: "Claude's ambience",
  Tasks: 'task list sync', Activity: 'activity line', Attention: 'hidden-window reminder',
}

const FEATURES: Record<string, string> = {
  Guard: '联机保护', Work: '工作状态同步', Waiting: '等待确认提醒', Timer: '计时器联动',
  Restore: '会话结束复原', Rain: '报错雨声', Status: '房间状态反显', Subagents: 'subagent 宠物',
  Notes: 'Claude 加 memo/todo', Pomodoro: 'Claude 开番茄钟', Music: 'Claude 换歌', Ambience: 'Claude 换环境音',
  Tasks: '任务清单同步', Activity: '头顶动态', Attention: '缩小时提醒',
}

export type RoomStatus = {
  updatedAt?: number
  ready?: boolean
  blocked?: boolean
  music?: string | null
  musicPlaying?: boolean
  timer?: string | null
  claudeTodaySeconds?: number
  health?: Record<string, string>
  // the room's language, chosen by the game mod: "zh" or "en"
  lang?: string
}

function duration(seconds: number): string {
  const m = Math.floor(seconds / 60)
  return m >= 60 ? `${Math.floor(m / 60)}h${String(m % 60).padStart(2, '0')}m` : `${m}m`
}

// One status-line row from the room.json the game mod writes every 2 s.
// Only symbols that are emoji by default (🏠 🎵 ⏰ 🍅): terminals agree they are two cells wide. Symbols such as
// ⏸ ⏱ ⚠ ♪ have no agreed width and push the rest of the line around; older game mods still send ⏸.
function plainTimer(timer: string, zh: boolean): string {
  return timer.replace(/\s*⏸/g, zh ? ' (暂停)' : ' (paused)')
}

// The summary of the room, in the room's language (room.json says which; a room.json left behind by a closed game
// still does). compact: for the desktop app, where the line shares a row with the model picker and gets cut early —
// no song, the timer without the pomodoro, and broken features only counted.
export function statusLine(room: RoomStatus | undefined, now: number, compact = false): string {
  const zh = room?.lang !== 'en'
  const t = (z: string, e: string) => (zh ? z : e)
  if (!room || typeof room.updatedAt !== 'number' || now - room.updatedAt > 10_000)
    return compact ? t('🏠 房间离线', '🏠 Room offline')
      : t('🏠 房间离线 (游戏没开，或 CozyClaude mod 没加载)', '🏠 Room offline (game closed, or the CozyClaude mod not loaded)')
  const parts: string[] = []
  if (!room.ready) parts.push(t('房间加载中', 'Room loading'))
  if (room.blocked) parts.push(compact ? t('联机中', 'Co-op') : t('联机中，暂停联动', 'In co-op, link paused'))
  if (room.music && !compact) parts.push(`🎵 ${room.music}${room.musicPlaying ? '' : t(' (暂停)', ' (paused)')}`)
  if (room.timer) parts.push(`⏰ ${plainTimer(compact ? room.timer.split(' · ')[0] ?? room.timer : room.timer, zh)}`)
  if (typeof room.claudeTodaySeconds === 'number')
    parts.push(t(`今日 Claude ${duration(room.claudeTodaySeconds)}`, `Claude today ${duration(room.claudeTodaySeconds)}`))
  const broken = Object.entries(room.health ?? {}).filter(([, value]) => value.startsWith('incompatible'))
  if (compact && broken.length > 0) parts.push(t(`注意：${broken.length} 项不兼容`, `Note: ${broken.length} incompatible`))
  if (!compact)
    for (const [key] of broken)
      parts.push(t(`注意：${FEATURES[key] ?? key}不兼容`, `Note: ${FEATURES_EN[key] ?? key} incompatible`))
  return `🏠 ${parts.join(' · ')}`
}
