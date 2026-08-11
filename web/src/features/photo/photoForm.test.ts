import { describe, expect, it } from 'vitest'
import { emptyPhotoCaptureFormValues, validatePhotoCaptureFormValues } from './photoForm'

describe('emptyPhotoCaptureFormValues', () => {
  it('starts with both optional fields blank', () => {
    expect(emptyPhotoCaptureFormValues()).toEqual({ activityId: '', caption: '' })
  })
})

describe('validatePhotoCaptureFormValues', () => {
  it('accepts fully blank values (both fields optional)', () => {
    expect(validatePhotoCaptureFormValues({ activityId: '', caption: '' })).toBeNull()
  })

  it('accepts a well-formed GUID activityId', () => {
    expect(
      validatePhotoCaptureFormValues({ activityId: '3fa85f64-5717-4562-b3fc-2c963f66afa6', caption: '' }),
    ).toBeNull()
  })

  it('rejects a malformed activityId', () => {
    expect(validatePhotoCaptureFormValues({ activityId: 'not-a-guid', caption: '' })).toMatch(/GUID/)
  })

  it('rejects a caption over 500 characters', () => {
    expect(validatePhotoCaptureFormValues({ activityId: '', caption: 'x'.repeat(501) })).toMatch(/500/)
  })

  it('accepts a caption exactly at the 500-character limit', () => {
    expect(validatePhotoCaptureFormValues({ activityId: '', caption: 'x'.repeat(500) })).toBeNull()
  })
})
