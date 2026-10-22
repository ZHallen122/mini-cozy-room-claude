// Pure session-state logic, kept apart from the engine calls so it can be tested on its own.

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
}

export type CozyEvent =
  | { kind: 'SessionStart'; source?: string }
  | { kind: 'UserPromptSubmit' }
  | { kind: 'PreToolUse'; agent?: string }
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
    agents: new Set(), ended: false,
  }
}

export function apply(s: SessionState, ev: CozyEvent, now: number): void {
  s.eventAt = now
  const who = 'agent' in ev && ev.agent ? ev.agent : 'main'
  switch (ev.kind) {
    case 'SessionStart':
      s.ended = false; s.working = false; s.waiting.clear(); s.error = false
      if (ev.source !== 'compact') s.agents.clear()
      break
    case 'UserPromptSubmit':
      s.working = true; s.waiting.clear(); s.error = false
      break
    case 'PreToolUse':
      s.working = true; s.waiting.delete(who)
      break
    case 'PermissionRequest':
      s.working = true; s.waiting.add(who)
      break
    case 'Notification':
      if (ev.type === 'permission_prompt' || ev.type === 'elicitation_dialog') s.waiting.add(who)
      break
    case 'PostToolUse':
      // a tool that succeeds is the "back to normal" signal for the rain
      s.working = true; s.waiting.delete(who); s.error = false
      break
    case 'PostToolUseFailure':
      s.working = true; s.waiting.delete(who)
      if (!ev.interrupted) { s.error = true; s.errorAt = now }
      break
    case 'Stop':
      s.working = false; s.waiting.clear(); s.error = false
      break
    case 'StopFailure':
      s.working = false; s.waiting.clear(); s.error = true; s.errorAt = now
      break
    case 'SubagentStart':
      if (ev.agent) s.agents.add(ev.agent)
      break
    case 'SubagentStop':
      if (ev.agent) { s.agents.delete(ev.agent); s.waiting.delete(ev.agent) }
      break
    case 'SessionEnd':
      s.ended = true; s.working = false; s.waiting.clear(); s.agents.clear()
      break
  }
}

// The file the game mod reads. `updatedAt` doubles as a heartbeat: a session that stops writing is gone.
export function serialize(s: SessionState, token: string, now: number): string {
  return JSON.stringify({
    v: 1, tok: token, sid: s.sid, startedAt: s.startedAt, updatedAt: now, eventAt: s.eventAt,
    working: s.working, waiting: s.waiting.size, error: s.error, errorAt: s.errorAt,
    agents: s.agents.size, ended: s.ended,
  })
}

// session ids are UUIDs; anything else is reduced to a safe file name
export function fileName(sid: string): string {
  return sid.replace(/[^A-Za-z0-9_-]/g, '_').slice(0, 80) + '.json'
}

const FEATURES: Record<string, string> = {
  Guard: '联机保护', Work: '工作状态同步', Waiting: '等待确认提醒', Timer: '计时器联动',
  Restore: '会话结束复原', Rain: '报错雨声', Status: '房间状态反显', Subagents: 'subagent 宠物',
  Notes: 'Claude 加 memo/todo', Pomodoro: 'Claude 开番茄钟', Music: 'Claude 换歌', Ambience: 'Claude 换环境音',
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
}

function duration(seconds: number): string {
  const m = Math.floor(seconds / 60)
  return m >= 60 ? `${Math.floor(m / 60)}h${String(m % 60).padStart(2, '0')}m` : `${m}m`
}

// One status-line row from the room.json the game mod writes every 2 s.
export function statusLine(room: RoomStatus | undefined, now: number): string {
  if (!room || typeof room.updatedAt !== 'number' || now - room.updatedAt > 10_000)
    return '🏠 房间离线（游戏没开，或 CozyClaude mod 没加载）'
  const parts = ['🏠']
  if (!room.ready) parts.push('房间加载中')
  if (room.blocked) parts.push('联机中·暂停联动')
  if (room.music) parts.push(`${room.musicPlaying ? '♪' : '⏸'} ${room.music}`)
  if (room.timer) parts.push(`⏱ ${room.timer}`)
  if (typeof room.claudeTodaySeconds === 'number') parts.push(`今日 Claude ${duration(room.claudeTodaySeconds)}`)
  for (const [key, value] of Object.entries(room.health ?? {}))
    if (value.startsWith('incompatible')) parts.push(`⚠ ${FEATURES[key] ?? key}不兼容`)
  return parts.join(' · ')
}
