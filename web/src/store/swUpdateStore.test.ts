import { beforeEach, describe, expect, it } from 'vitest'
import { useSwUpdateStore } from './swUpdateStore'

describe('swUpdateStore (S13-FE-02)', () => {
  beforeEach(() => {
    useSwUpdateStore.setState({ updateAvailable: false })
  })

  it('defaults to no update available', () => {
    expect(useSwUpdateStore.getState().updateAvailable).toBe(false)
  })

  it('setUpdateAvailable(true) flips the flag', () => {
    useSwUpdateStore.getState().setUpdateAvailable(true)
    expect(useSwUpdateStore.getState().updateAvailable).toBe(true)
  })

  it('setUpdateAvailable(false) clears it again (e.g. after the user activates the update)', () => {
    useSwUpdateStore.getState().setUpdateAvailable(true)
    useSwUpdateStore.getState().setUpdateAvailable(false)
    expect(useSwUpdateStore.getState().updateAvailable).toBe(false)
  })
})
