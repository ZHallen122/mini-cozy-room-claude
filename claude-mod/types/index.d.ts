// The room summary for the hint line under the prompt, in versions from longest to shortest (the render hook keeps
// the longest that fits the terminal); null when there is nothing to show.
export type RoomLine = string[] | null

declare module 'claude-code' {
  interface PluginState {
    'cozy-room': { roomLine: RoomLine }
  }
}
