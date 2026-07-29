import { beforeEach, describe, expect, it } from 'vitest'
import { pushToast, useToastStore } from './toastStore'

describe('toastStore', () => {
  beforeEach(() => {
    useToastStore.setState({ toasts: [] })
  })

  it('show() enqueues a toast with a generated id', () => {
    useToastStore.getState().show({ message: 'hello' })

    const { toasts } = useToastStore.getState()
    expect(toasts).toHaveLength(1)
    expect(toasts[0]).toMatchObject({ message: 'hello' })
    expect(toasts[0].id).toMatch(/^toast-/)
  })

  it('dismiss() removes a toast by id without touching others', () => {
    useToastStore.getState().show({ message: 'one' })
    useToastStore.getState().show({ message: 'two' })
    const [first] = useToastStore.getState().toasts

    useToastStore.getState().dismiss(first.id)

    const { toasts } = useToastStore.getState()
    expect(toasts).toHaveLength(1)
    expect(toasts[0].message).toBe('two')
  })

  it('pushToast() works outside a React component (e.g. an axios interceptor)', () => {
    pushToast({ message: 'from outside' })
    expect(useToastStore.getState().toasts.at(-1)).toMatchObject({ message: 'from outside' })
  })
})
