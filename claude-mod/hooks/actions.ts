// v2 "active operations": the tools the model calls to act in the room when the user asks it to.
// Pure logic (tool specs, input checks, the room gate, reply text), kept apart from the engine calls in register.ts.

import type { RoomStatus } from './state'

export type CommandSpec = { op: string; args: Record<string, unknown> }

export type RoomTool = {
  name: string
  description: string
  inputSchema: Record<string, unknown>
  // the command for the game mod, or why the input can't become one
  toCommand: (input: Record<string, unknown>) => CommandSpec | { error: string }
}

const ROOM = 'Mini Cozy Room: Lo-Fi (the cozy room game on this PC, linked through the CozyClaude mod)'
const ONLY_WHEN_ASKED = 'Use only when the user asks for it in this conversation.'

function str(v: unknown): string | undefined {
  return typeof v === 'string' && v.trim() !== '' ? v.trim() : undefined
}

function strings(v: unknown): string[] {
  const list = Array.isArray(v) ? v : typeof v === 'string' ? [v] : []
  return list.filter((x): x is string => typeof x === 'string' && x.trim() !== '').map(x => x.trim())
}

function oneOf(v: unknown, allowed: readonly string[], what: string): string | { error: string } {
  const s = str(v)
  return s !== undefined && allowed.includes(s) ? s : { error: `${what} must be one of: ${allowed.join(', ')}` }
}

const MUSIC_ACTIONS = ['list', 'play', 'next', 'previous', 'pause', 'resume'] as const
const AMBIENCE_ACTIONS = ['list', 'set', 'add', 'stop'] as const
const TIMER_ACTIONS = ['start', 'pause', 'status'] as const

export const TOOLS: RoomTool[] = [
  {
    name: 'room_note',
    description:
      `Adds a memo or a todo list in ${ROOM}. Its title gets a "[Claude] " prefix so the user can tell it apart, ` +
      `and room_clear_claude_notes removes it again. It never edits the user's own memos or todo lists. ${ONLY_WHEN_ASKED}`,
    inputSchema: {
      type: 'object',
      properties: {
        type: { type: 'string', enum: ['memo', 'todo'], description: 'memo: a text note; todo: a checklist' },
        title: { type: 'string', description: 'short title, up to 40 characters' },
        content: { type: 'string', description: 'memo text (type memo), up to 4000 characters' },
        items: { type: 'array', items: { type: 'string' }, description: 'todo items (type todo), up to 30' },
      },
      required: ['type'],
    },
    toCommand: input => {
      const type = oneOf(input.type, ['memo', 'todo'], 'type')
      if (typeof type !== 'string') return type
      const title = str(input.title)
      if (type === 'memo') {
        const content = str(input.content) ?? strings(input.items).join('\n')
        if (!content) return { error: 'a memo needs content' }
        return { op: 'memo.add', args: { title, content } }
      }
      const items = strings(input.items)
      if (items.length === 0) {
        const fromContent = (str(input.content) ?? '').split(/\r?\n/).map(s => s.replace(/^\s*[-*]\s*/, '').trim()).filter(Boolean)
        if (fromContent.length === 0) return { error: 'a todo list needs items' }
        return { op: 'todo.add', args: { title, items: fromContent } }
      }
      return { op: 'todo.add', args: { title, items } }
    },
  },
  {
    name: 'room_clear_claude_notes',
    description:
      `Removes every memo and todo list that Claude added in ${ROOM} (the ones still titled "[Claude] ..."). ` +
      `Notes the user wrote, or retitled, are never touched. ${ONLY_WHEN_ASKED}`,
    inputSchema: { type: 'object', properties: {} },
    toCommand: () => ({ op: 'notes.clear', args: {} }),
  },
  {
    name: 'room_timer',
    description:
      `Starts, pauses or reads the pomodoro (focus/break session timer) in ${ROOM}. It uses the user's own timer ` +
      `settings. ${ONLY_WHEN_ASKED}`,
    inputSchema: {
      type: 'object',
      properties: { action: { type: 'string', enum: [...TIMER_ACTIONS] } },
      required: ['action'],
    },
    toCommand: input => {
      const action = oneOf(input.action, TIMER_ACTIONS, 'action')
      return typeof action === 'string' ? { op: 'timer', args: { action } } : action
    },
  },
  {
    name: 'room_music',
    description:
      `Controls the music player in ${ROOM}: list the songs the user owns, play one by name, skip to the next or ` +
      `previous song, pause or resume. Songs from DLC the user hasn't bought are never played. ` +
      `Call action "list" first when you need a song's exact name. ${ONLY_WHEN_ASKED}`,
    inputSchema: {
      type: 'object',
      properties: {
        action: { type: 'string', enum: [...MUSIC_ACTIONS] },
        name: { type: 'string', description: 'song name (or number key) for action "play"' },
      },
      required: ['action'],
    },
    toCommand: input => {
      const action = oneOf(input.action, MUSIC_ACTIONS, 'action')
      if (typeof action !== 'string') return action
      const name = str(input.name)
      if (action === 'play' && !name) return { error: 'action "play" needs a song name; use action "list" to see them' }
      return { op: 'music', args: { action, name } }
    },
  },
  {
    name: 'room_ambience',
    description:
      `Changes the ambient sounds in ${ROOM} (rain, fireplace, birds, ...): list them, set exactly these to play, ` +
      `add some, or stop some (or all). Names are the game's own (in the game's language; action "list" shows ` +
      `them). DLC sounds the user hasn't bought are never played. If the ambience is muted, set/add unmutes it. ` +
      ONLY_WHEN_ASKED,
    inputSchema: {
      type: 'object',
      properties: {
        action: { type: 'string', enum: [...AMBIENCE_ACTIONS] },
        names: { type: 'array', items: { type: 'string' }, description: 'sound names or number keys' },
      },
      required: ['action'],
    },
    toCommand: input => {
      const action = oneOf(input.action, AMBIENCE_ACTIONS, 'action')
      if (typeof action !== 'string') return action
      const names = strings(input.names)
      if ((action === 'set' || action === 'add') && names.length === 0)
        return { error: `action "${action}" needs names; use action "list" to see them` }
      return { op: 'ambience', args: { action, names } }
    },
  },
]

export function findTool(name: string): RoomTool | undefined {
  return TOOLS.find(t => t.name === name)
}

// Why the room can't take a command right now, without asking it; undefined when it might.
export function roomGate(room: RoomStatus | undefined, now: number): string | undefined {
  if (!room || typeof room.updatedAt !== 'number' || now - room.updatedAt > 10_000)
    return 'Mini Cozy Room is not running, or the CozyClaude mod is not loaded (room.json is missing or stale). Nothing was changed.'
  if (room.blocked)
    return 'The user is in a co-op (multiplayer) room or lobby; CozyClaude never changes anything there. Nothing was changed.'
  if (!room.ready) return 'The game is still on its title screen or loading the room. Nothing was changed; try again shortly.'
  return undefined
}

export type CommandReply = { id?: string; ok?: boolean; message?: string; data?: unknown }

// The text the model reads for a game reply.
export function replyText(reply: CommandReply): string {
  const message = typeof reply.message === 'string' && reply.message !== '' ? reply.message : reply.ok ? 'Done.' : 'Failed.'
  if (reply.data === undefined || reply.data === null) return message
  const data = JSON.stringify(reply.data)
  return `${message}\n${data.length > 6000 ? data.slice(0, 6000) + '…' : data}`
}

export function commandFile(id: string, token: string, cmd: CommandSpec, now: number): string {
  return JSON.stringify({ v: 1, tok: token, id, op: cmd.op, args: cmd.args, at: now })
}

export const TIMEOUT_TEXT =
  'Mini Cozy Room did not answer within 5 seconds (the game may be frozen, minimized into a loading screen, or the ' +
  'CozyClaude mod stopped reading commands). The request was withdrawn and will not run later.'
