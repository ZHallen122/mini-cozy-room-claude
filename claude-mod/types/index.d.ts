// The room summary appended to the hint line under the prompt; null when there is nothing to show.
export type RoomLine = string | null

declare module 'claude-code' {
  interface PluginState {
    'cozy-room': { roomLine: RoomLine }
  }
}
