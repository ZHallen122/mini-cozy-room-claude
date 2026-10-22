import { describe, expect, test } from 'claude-code/testing'
import { commandFile, findTool, replyText, roomGate, TOOLS } from './actions'

function cmd(tool: string, input: Record<string, unknown>) {
  const t = findTool(tool)
  if (!t) throw new Error('no tool ' + tool)
  return t.toCommand(input)
}

describe('room tools', () => {
  test('every tool has a valid short name and an object schema', async () => {
    for (const t of TOOLS) {
      expect(/^[A-Za-z0-9_-]{1,64}$/.test(t.name)).toBe(true)
      expect(t.inputSchema.type).toBe('object')
      expect(t.description.length > 40).toBe(true)
    }
  })

  test('a memo needs content; a todo takes items or lines of content', async () => {
    expect(cmd('room_note', { type: 'memo', title: 'x' })).toEqual({ error: 'a memo needs content' })
    expect(cmd('room_note', { type: 'memo', title: 't', content: ' hi ' })).toEqual({ op: 'memo.add', args: { title: 't', content: 'hi' } })
    expect(cmd('room_note', { type: 'todo', items: ['a', ' ', 'b'] })).toEqual({ op: 'todo.add', args: { title: undefined, items: ['a', 'b'] } })
    expect(cmd('room_note', { type: 'todo', content: '- a\n- b\n' })).toEqual({ op: 'todo.add', args: { title: undefined, items: ['a', 'b'] } })
    expect('error' in cmd('room_note', { type: 'todo' })).toBe(true)
    expect('error' in cmd('room_note', { type: 'card' })).toBe(true)
  })

  test('actions are checked before anything reaches the game', async () => {
    expect(cmd('room_timer', { action: 'start' })).toEqual({ op: 'timer', args: { action: 'start' } })
    expect('error' in cmd('room_timer', { action: 'reset' })).toBe(true)
    expect('error' in cmd('room_music', { action: 'play' })).toBe(true)
    expect(cmd('room_music', { action: 'next' })).toEqual({ op: 'music', args: { action: 'next', name: undefined } })
    expect('error' in cmd('room_ambience', { action: 'set', names: [] })).toBe(true)
    expect(cmd('room_ambience', { action: 'set', names: '雨声' })).toEqual({ op: 'ambience', args: { action: 'set', names: ['雨声'] } })
    expect(cmd('room_ambience', { action: 'stop' })).toEqual({ op: 'ambience', args: { action: 'stop', names: [] } })
  })

  test('the gate refuses an offline, co-op or loading room without asking it', async () => {
    const now = 100_000
    expect(roomGate(undefined, now)).toContain('not running')
    expect(roomGate({ updatedAt: now - 20_000, ready: true }, now)).toContain('not running')
    expect(roomGate({ updatedAt: now, ready: true, blocked: true }, now)).toContain('co-op')
    expect(roomGate({ updatedAt: now, ready: false }, now)).toContain('loading')
    expect(roomGate({ updatedAt: now - 1_000, ready: true }, now)).toBe(undefined)
  })

  test('commands carry the token and a timestamp; replies read as text', async () => {
    const file = JSON.parse(commandFile('id1', 'tok', { op: 'music', args: { action: 'next' } }, 5))
    expect(file).toEqual({ v: 1, tok: 'tok', id: 'id1', op: 'music', args: { action: 'next' }, at: 5 })
    expect(replyText({ ok: true, message: '好了' })).toBe('好了')
    expect(replyText({ ok: true, message: 'm', data: { a: 1 } })).toBe('m\n{"a":1}')
    expect(replyText({ ok: false })).toBe('Failed.')
  })
})
