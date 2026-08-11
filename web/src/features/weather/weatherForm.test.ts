import { describe, expect, it } from 'vitest'
import {
  buildWeatherLogRequestFields,
  emptyWeatherLogFormValues,
  validateWeatherLogFormValues,
  weatherLogFormValuesFromEntry,
} from './weatherForm'
import type { WeatherLogDto } from './types'

describe('features/weather/weatherForm', () => {
  describe('validateWeatherLogFormValues', () => {
    it('requires a log date', () => {
      const values = { ...emptyWeatherLogFormValues(), logDate: '' }
      expect(validateWeatherLogFormValues(values)).toMatch(/วันที่/)
    })

    // domain-rules.md §3.4: "HoursLost is required by FluentValidation when Impact <> NoImpact".
    it('requires hoursLost when impact is not NoImpact', () => {
      const values = { ...emptyWeatherLogFormValues(), impact: 'FullStoppage' as const, hoursLost: '' }
      expect(validateWeatherLogFormValues(values)).toMatch(/ชั่วโมง/)
    })

    it('does not require hoursLost when impact is NoImpact', () => {
      const values = { ...emptyWeatherLogFormValues(), impact: 'NoImpact' as const, hoursLost: '' }
      expect(validateWeatherLogFormValues(values)).toBeNull()
    })

    it('rejects hoursLost outside 0-24', () => {
      const values = { ...emptyWeatherLogFormValues(), impact: 'FullStoppage' as const, hoursLost: '25' }
      expect(validateWeatherLogFormValues(values)).toMatch(/0-24/)
    })

    it('accepts hoursLost exactly at the boundary (0 and 24)', () => {
      expect(validateWeatherLogFormValues({ ...emptyWeatherLogFormValues(), impact: 'FullStoppage', hoursLost: '0' })).toBeNull()
      expect(validateWeatherLogFormValues({ ...emptyWeatherLogFormValues(), impact: 'FullStoppage', hoursLost: '24' })).toBeNull()
    })

    it('rejects a negative rainfall value', () => {
      const values = { ...emptyWeatherLogFormValues(), rainfallMm: '-1' }
      expect(validateWeatherLogFormValues(values)).toMatch(/ฝน/)
    })

    it('accepts a fully blank optional-only form (NoImpact, no rainfall)', () => {
      expect(validateWeatherLogFormValues(emptyWeatherLogFormValues())).toBeNull()
    })
  })

  describe('buildWeatherLogRequestFields', () => {
    it('converts blank optional text fields to null, never empty strings', () => {
      const fields = buildWeatherLogRequestFields(emptyWeatherLogFormValues())
      expect(fields.conditionNote).toBeNull()
      expect(fields.impactNote).toBeNull()
      expect(fields.rainfallMm).toBeNull()
      expect(fields.hoursLost).toBeNull()
      expect(fields.affectedActivityIds).toEqual([])
    })

    it('round-trips the log date as a UTC-midnight ISO instant', () => {
      const fields = buildWeatherLogRequestFields({ ...emptyWeatherLogFormValues(), logDate: '2026-07-08' })
      expect(fields.logDate).toBe('2026-07-08T00:00:00.000Z')
    })

    it('trims whitespace-only optional fields to null', () => {
      const fields = buildWeatherLogRequestFields({ ...emptyWeatherLogFormValues(), conditionNote: '   ' })
      expect(fields.conditionNote).toBeNull()
    })

    it('preserves a real numeric string for rainfall/hoursLost untouched', () => {
      const fields = buildWeatherLogRequestFields({
        ...emptyWeatherLogFormValues(),
        rainfallMm: '61.00',
        hoursLost: '8.00',
        impact: 'FullStoppage',
      })
      expect(fields.rainfallMm).toBe('61.00')
      expect(fields.hoursLost).toBe('8.00')
    })
  })

  describe('weatherLogFormValuesFromEntry', () => {
    it('pre-fills every field from the target entry, including its affected activities', () => {
      const entry: WeatherLogDto = {
        id: 'e1',
        projectId: 'project-1',
        logDate: '2026-07-08T00:00:00+07:00',
        condition: 'HeavyRain',
        conditionNote: 'ฝนตกต่อเนื่อง',
        rainfallMm: '61.00',
        impact: 'FullStoppage',
        impactNote: 'หยุดงานภายนอกทั้งวัน',
        hoursLost: '8.00',
        workStoppage: true,
        entryKind: 'Original',
        correctsWeatherLogId: null,
        correctionReason: null,
        affectedActivityIds: ['activity-1', 'activity-2'],
        recordedByUserId: 'user-1',
        recordedAt: '2026-07-08T09:00:00+07:00',
      }

      const values = weatherLogFormValuesFromEntry(entry)
      expect(values.logDate).toBe('2026-07-08')
      expect(values.condition).toBe('HeavyRain')
      expect(values.conditionNote).toBe('ฝนตกต่อเนื่อง')
      expect(values.rainfallMm).toBe('61.00')
      expect(values.impact).toBe('FullStoppage')
      expect(values.impactNote).toBe('หยุดงานภายนอกทั้งวัน')
      expect(values.hoursLost).toBe('8.00')
      expect(values.activityIds).toEqual(['activity-1', 'activity-2'])
    })

    it('renders a null optional field as an empty string, never the literal "null"', () => {
      const entry: WeatherLogDto = {
        id: 'e1',
        projectId: 'project-1',
        logDate: '2026-07-08T00:00:00+07:00',
        condition: 'Clear',
        conditionNote: null,
        rainfallMm: null,
        impact: 'NoImpact',
        impactNote: null,
        hoursLost: null,
        workStoppage: false,
        entryKind: 'Original',
        correctsWeatherLogId: null,
        correctionReason: null,
        affectedActivityIds: [],
        recordedByUserId: 'user-1',
        recordedAt: '2026-07-08T09:00:00+07:00',
      }

      const values = weatherLogFormValuesFromEntry(entry)
      expect(values.conditionNote).toBe('')
      expect(values.rainfallMm).toBe('')
      expect(values.impactNote).toBe('')
      expect(values.hoursLost).toBe('')
    })
  })
})
