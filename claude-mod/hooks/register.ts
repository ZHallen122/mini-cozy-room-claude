import { atom, read, update } from 'claude-code'
import type { EngineInterface, Register } from 'claude-code'
import {
  apply, type CozyEvent, describeTool, fileName, fresh, projectName, type RoomStatus, type SessionState, serialize, statusLine,
  taskIdFrom,
} from './state'
import { type CommandReply, commandFile, replyText, type RoomTool, roomGate, TIMEOUT_TEXT, TOOLS } from './actions'

// Bridges Claude Code to the CozyClaude game mod through %LOCALAPPDATA%\CozyClaude:
//   sessions\<session>.json  this session's state, rewritten on every event and every 20 s as a heartbeat
//   secret.key               random token every session file carries; the game mod ignores files without it
//   room.json                written by the game mod, read back here for the status line
//   commands\<id>.json       v2: a room operation the model asked for (signed with the token, expires in 8 s)
//   results\<id>.json        v2: the game mod's answer to it
// Every hook hands the event on untouched and does its file work afterwards, unawaited, so a missing game,
// a full disk or a bug here never slows down or changes what Claude Code does.

const HEARTBEAT_MS = 20_000
const STATUS_MS = 3_000
// A tool call waits this long for the game. Sleeps count against the hook's 10 s budget, so stay well under it.
const REPLY_WAIT_MS = 5_000
const REPLY_POLL_MS = 150
const PLUGIN = 'cozy-room'

// The room summary drawn at the end of the hint line under the prompt. Kept in $.state so the line redraws when
// it changes (a render hook may only read).
const roomLine = atom({ plugin: 'cozy-room', key: 'roomLine' } as const, null)
// The terminal draws the hint line's tail; other surfaces don't, so there the summary stays a pinned status.
let tailDrawn = false

const sessions = new Map<string, SessionState>()
let root: string | undefined
let token: string | undefined
let queue: Promise<void> = Promise.resolve()
let timersStarted = false

async function ensureRoot($: EngineInterface): Promise<string> {
  if (root) return root
  const base = await $.env.get('LOCALAPPDATA')
  if (!base) throw new Error('LOCALAPPDATA is not set')
  root = `${base}\\CozyClaude`
  return root
}

async function ensureToken($: EngineInterface): Promise<string> {
  if (token) return token
  const path = `${await ensureRoot($)}\\secret.key`
  if (await $.fs.exists(path)) token = (await $.fs.read(path)).trim()
  if (!token) {
    const bytes = crypto.getRandomValues(new Uint8Array(32))
    token = Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('')
    await $.fs.write(path, token)
  }
  return token
}

// Writes go one after another so an older state can never land after a newer one.
function persist($: EngineInterface, s: SessionState): void {
  queue = queue
    .then(async () => {
      const tok = await ensureToken($)
      const dir = `${await ensureRoot($)}\\sessions`
      await $.fs.write(`${dir}\\${fileName(s.sid)}`, serialize(s, tok, Date.now()))
    })
    .catch(() => {})
}

function record($: EngineInterface, sid: string | undefined, ev: CozyEvent, cwd?: string): void {
  try {
    if (!sid) return
    let s = sessions.get(sid)
    if (!s) { s = fresh(sid, Date.now()); sessions.set(sid, s) }
    if (cwd) s.project = projectName(cwd)
    apply(s, ev, Date.now())
    persist($, s)
    startTimers($)
  } catch {
    // never let the bridge disturb Claude Code
  }
}

function recordCurrent($: EngineInterface, events: CozyEvent[]): void {
  $.session.id().then(sid => { for (const ev of events) record($, sid, ev) }, () => {})
}

// What a tool call tells the room: the bubble line, and task-list changes for TaskUpdate / TodoWrite.
function preToolEvents(tool: string, input: Record<string, unknown>): CozyEvent[] {
  const events: CozyEvent[] = [{ kind: 'PreToolUse', activity: describeTool(tool, input) }]
  const str = (v: unknown) => (typeof v === 'string' ? v : undefined)
  if (tool === 'TaskUpdate' && (typeof input.taskId === 'string' || typeof input.taskId === 'number'))
    events.push({ kind: 'TaskUpdate', id: String(input.taskId), status: str(input.status), subject: str(input.subject) })
  if (tool === 'TodoWrite' && Array.isArray(input.todos))
    events.push({
      kind: 'TodoWrite',
      todos: input.todos.map(t => ({ content: str((t as Record<string, unknown>)?.content) ?? '', status: str((t as Record<string, unknown>)?.status) ?? '' })),
    })
  return events
}

function startTimers($: EngineInterface): void {
  if (timersStarted) return
  timersStarted = true
  $.clock.every(HEARTBEAT_MS, () => {
    for (const s of sessions.values()) if (!s.ended) persist($, s)
  })
  $.clock.every(STATUS_MS, () => { void refreshStatus($) })
  void refreshStatus($)
}

async function refreshStatus($: EngineInterface): Promise<void> {
  try {
    const room = await readRoom($)
    const line = statusLine(room, Date.now())
    if ((await read($, roomLine)) !== line) await update($, roomLine, () => line)
    // the pinned status is what the desktop app shows, in a narrow slot beside the model picker: the short form
    $.ui.status(tailDrawn ? undefined : statusLine(room, Date.now(), true))
  } catch {
    // leave the previous line in place
  }
}

async function readRoom($: EngineInterface): Promise<RoomStatus | undefined> {
  try { return JSON.parse(await $.fs.read(`${await ensureRoot($)}\\room.json`)) as RoomStatus } catch { return undefined }
}

async function registerTools($: EngineInterface): Promise<void> {
  for (const t of TOOLS) {
    try {
      await $.tool.register({ name: t.name, description: t.description, inputSchema: t.inputSchema })
    } catch {
      // a session that can't take the tools still gets the room sync
    }
  }
}

// One room operation: write the signed command, wait up to REPLY_WAIT_MS for the game's answer. Every way this
// can fail ends in a { deny } with the reason, which the model reads as the tool's error; nothing here can hang.
async function runRoomTool($: EngineInterface, tool: RoomTool, input: Record<string, unknown>, signal: AbortSignal) {
  const cmd = tool.toCommand(input)
  if ('error' in cmd) return { deny: cmd.error }
  const gate = roomGate(await readRoom($), Date.now())
  if (gate) return { deny: gate }
  const dir = await ensureRoot($)
  const tok = await ensureToken($)
  const id = crypto.randomUUID()
  const cmdPath = `${dir}\\commands\\${id}.json`
  const resultPath = `${dir}\\results\\${id}.json`
  await $.fs.write(cmdPath, commandFile(id, tok, cmd, Date.now()))
  let answered = false
  try {
    const deadline = Date.now() + REPLY_WAIT_MS
    while (Date.now() < deadline) {
      await $.clock.sleep(REPLY_POLL_MS, { signal })
      if (!(await $.fs.exists(resultPath))) continue
      let reply: CommandReply
      try { reply = JSON.parse(await $.fs.read(resultPath)) as CommandReply } catch { continue }
      answered = true
      return reply.ok ? { result: replyText(reply) } : { deny: replyText(reply) }
    }
    return { deny: TIMEOUT_TEXT }
  } finally {
    // withdraw an unanswered command (timeout or interrupt) so the game never runs it late
    if (!answered) await $.fs.write(cmdPath, JSON.stringify({ v: 1, id, cancelled: true })).catch(() => {})
  }
}

// Each hook's .catch passes the event on as if the plugin were not installed, should the hook ever fail.
export const register: Register = on => {
  on('session.start', async ($, e, next) => {
    startTimers($)
    await registerTools($)
    return next(e)
  }).catch(($, e, next) => next(e))

  on('ui.render', { component: 'PromptHint' }, async ($, e, next) => {
    const line = await read($, roomLine)
    if (!line || e.surface !== 'terminal') return next(e)
    tailDrawn = true
    // the engine puts its own " · " before the tail
    return next({ ...e, props: { ...e.props, tail: line } })
  }).catch(($, e, next) => next(e))

  for (const t of TOOLS) {
    on('tool.call', { tool: `mcp__${PLUGIN}__${t.name}` }, ($, e, next) =>
      runRoomTool($, t, e as unknown as Record<string, unknown>, next.signal),
    ).catch(($, e, next) => ({ deny: `${PLUGIN}: the room tool failed (${next.error?.message ?? 'unknown error'}). Nothing else was affected.` }))
  }

  on('classic.SessionStart', ($, e, next) => {
    record($, e.session_id, { kind: 'SessionStart', source: e.source }, e.cwd)
    return next(e)
  }).catch(($, e, next) => next(e))
  on('classic.UserPromptSubmit', ($, e, next) => {
    record($, e.session_id, { kind: 'UserPromptSubmit' }, e.cwd)
    return next(e)
  }).catch(($, e, next) => next(e))
  on('classic.PreToolUse', ($, e, next) => {
    try {
      const input = e as unknown as Record<string, unknown>
      recordCurrent($, preToolEvents(String(input.tool ?? ''), input))
    } catch {
      // the bubble line is cosmetic
    }
    return next(e)
  }).catch(($, e, next) => next(e))
  on('classic.PermissionRequest', ($, e, next) => {
    record($, e.session_id, { kind: 'PermissionRequest', agent: e.agent_id }, e.cwd)
    return next(e)
  }).catch(($, e, next) => next(e))
  on('classic.Notification', ($, e, next) => {
    record($, e.session_id, { kind: 'Notification', agent: e.agent_id, type: e.notification_type }, e.cwd)
    return next(e)
  }).catch(($, e, next) => next(e))
  on('classic.PostToolUse', ($, e, next) => {
    record($, e.session_id, { kind: 'PostToolUse', agent: e.agent_id }, e.cwd)
    if (e.tool_name === 'TaskCreate') {
      // backs up TaskCreated, in case that event doesn't fire for this kind of task
      const input = (e.tool_input ?? {}) as Record<string, unknown>
      const id = taskIdFrom(e.tool_response)
      if (id && typeof input.subject === 'string') record($, e.session_id, { kind: 'TaskCreated', id, subject: input.subject })
    }
    return next(e)
  }).catch(($, e, next) => next(e))
  on('classic.TaskCreated', ($, e, next) => {
    record($, e.session_id, { kind: 'TaskCreated', id: e.task_id, subject: e.task_subject }, e.cwd)
    return next(e)
  }).catch(($, e, next) => next(e))
  on('classic.TaskCompleted', ($, e, next) => {
    record($, e.session_id, { kind: 'TaskCompleted', id: e.task_id, subject: e.task_subject }, e.cwd)
    return next(e)
  }).catch(($, e, next) => next(e))
  on('classic.PostToolUseFailure', ($, e, next) => {
    record($, e.session_id, { kind: 'PostToolUseFailure', agent: e.agent_id, interrupted: e.is_interrupt === true }, e.cwd)
    return next(e)
  }).catch(($, e, next) => next(e))
  on('classic.Stop', ($, e, next) => {
    record($, e.session_id, { kind: 'Stop' }, e.cwd)
    return next(e)
  }).catch(($, e, next) => next(e))
  on('classic.StopFailure', ($, e, next) => {
    record($, e.session_id, { kind: 'StopFailure' }, e.cwd)
    return next(e)
  }).catch(($, e, next) => next(e))
  on('classic.SubagentStart', ($, e, next) => {
    record($, e.session_id, { kind: 'SubagentStart', agent: e.agent_id }, e.cwd)
    return next(e)
  }).catch(($, e, next) => next(e))
  on('classic.SubagentStop', ($, e, next) => {
    record($, e.session_id, { kind: 'SubagentStop', agent: e.agent_id }, e.cwd)
    return next(e)
  }).catch(($, e, next) => next(e))
  on('classic.SessionEnd', ($, e, next) => {
    record($, e.session_id, { kind: 'SessionEnd' }, e.cwd)
    return next(e)
  }).catch(($, e, next) => next(e))
}
