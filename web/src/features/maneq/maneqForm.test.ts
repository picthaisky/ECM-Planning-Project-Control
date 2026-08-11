import { describe, expect, it } from 'vitest'
import {
  buildRecordManpowerLogCorrectionPayload,
  buildRecordManpowerLogPayload,
  emptyManpowerLogFormValues,
  manpowerLogFormValuesFromEntry,
  validateManpowerLogFormValues,
} from './maneqForm'
import type { ManpowerLogFormValues } from './maneqForm'
import type { ManpowerLogDto } from './types'

const VALID_GUID = '3fa85f64-5717-4562-b3fc-2c963f66afa6'

function validValues(overrides: Partial<ManpowerLogFormValues> = {}): ManpowerLogFormValues {
  return {
    ...emptyManpowerLogFormValues(),
    workCategoryId: VALID_GUID,
    workerCount: '25',
    manHours: '200.00',
    overtimeHours: '10',
    equipmentCount: '2',
    equipmentOperatingHours: '12',
    equipmentStandbyHours: '4',
    ...overrides,
  }
}

describe('emptyManpowerLogFormValues', () => {
  it("defaults to today's date, Day shift, OwnDirect labour, zeroed hour fields", () => {
    const values = emptyManpowerLogFormValues()
    expect(values.shift).toBe('Day')
    expect(values.labourType).toBe('OwnDirect')
    expect(values.workCategoryId).toBe('')
    expect(values.manHoursDerived).toBe(false)
    expect(values.logDate).toMatch(/^\d{4}-\d{2}-\d{2}$/)
  })
})

describe('validateManpowerLogFormValues', () => {
  it('accepts a well-formed set of values (fixture-M-01-shaped)', () => {
    expect(validateManpowerLogFormValues(validValues())).toBeNull()
  })

  it('requires workCategoryId (§4.1: NOT NULL)', () => {
    expect(validateManpowerLogFormValues(validValues({ workCategoryId: '' }))).toMatch(/หมวดงาน/)
  })

  it('rejects a malformed workCategoryId', () => {
    expect(validateManpowerLogFormValues(validValues({ workCategoryId: 'not-a-guid' }))).toMatch(/GUID/)
  })

  it('rejects a malformed optional GUID field (wbsNodeId)', () => {
    expect(validateManpowerLogFormValues(validValues({ wbsNodeId: 'not-a-guid' }))).toMatch(/WBS Node/)
  })

  it('accepts optional GUID fields left blank', () => {
    expect(validateManpowerLogFormValues(validValues({ wbsNodeId: '', activityId: '', relatedWeatherLogId: '' }))).toBeNull()
  })

  it('rejects ManHours > WorkerCount * 24 (§4.1 rule 1)', () => {
    expect(validateManpowerLogFormValues(validValues({ workerCount: '1', manHours: '25' }))).toMatch(/24 ชั่วโมง/)
  })

  it('accepts ManHours exactly at WorkerCount * 24', () => {
    expect(validateManpowerLogFormValues(validValues({ workerCount: '1', manHours: '24' }))).toBeNull()
  })

  it('rejects ManHours > 0 with WorkerCount = 0 (§4.1 rule 2)', () => {
    expect(validateManpowerLogFormValues(validValues({ workerCount: '0', manHours: '8' }))).toMatch(/จำนวนคน/)
  })

  it('accepts WorkerCount = 0 and ManHours = 0 (a valid "งานหยุด" row, §4.1)', () => {
    expect(
      validateManpowerLogFormValues(validValues({ workerCount: '0', manHours: '0', overtimeHours: '0' })),
    ).toBeNull()
  })

  it('rejects OvertimeHours > ManHours (§4.1 rule 3: OT is a subset)', () => {
    expect(validateManpowerLogFormValues(validValues({ manHours: '8', overtimeHours: '10' }))).toMatch(/ล่วงเวลา/)
  })

  it('rejects EquipmentOperatingHours + EquipmentStandbyHours > EquipmentCount * 24 (§4.1 rule 4)', () => {
    expect(
      validateManpowerLogFormValues(validValues({ equipmentCount: '1', equipmentOperatingHours: '20', equipmentStandbyHours: '10' })),
    ).toMatch(/เครื่องจักร/)
  })

  it('rejects a negative number field', () => {
    expect(validateManpowerLogFormValues(validValues({ workerCount: '-1' }))).toMatch(/ไม่ติดลบ/)
  })

  it('rejects a blank required date', () => {
    expect(validateManpowerLogFormValues(validValues({ logDate: '' }))).toBe('กรุณาระบุวันที่')
  })
})

describe('buildRecordManpowerLogPayload', () => {
  it('trims text fields, converts blank optional GUIDs to null, and carries allowDuplicate through', () => {
    const payload = buildRecordManpowerLogPayload(validValues({ workDescription: '  โครงสร้าง ชั้น 9  ', wbsNodeId: '' }), true)
    expect(payload.workDescription).toBe('โครงสร้าง ชั้น 9')
    expect(payload.wbsNodeId).toBeNull()
    expect(payload.allowDuplicate).toBe(true)
    expect(payload.workerCount).toBe(25)
  })

  it('defaults allowDuplicate to false', () => {
    expect(buildRecordManpowerLogPayload(validValues()).allowDuplicate).toBe(false)
  })
})

describe('buildRecordManpowerLogCorrectionPayload', () => {
  it('includes entryKind and correctionReason alongside every log field', () => {
    const payload = buildRecordManpowerLogCorrectionPayload(validValues(), 'Correction', 'พิมพ์จำนวนคนผิด')
    expect(payload.entryKind).toBe('Correction')
    expect(payload.correctionReason).toBe('พิมพ์จำนวนคนผิด')
    expect(payload.workCategoryId).toBe(VALID_GUID)
  })
})

describe('manpowerLogFormValuesFromEntry', () => {
  it('round-trips every field from a ManpowerLogDto into form values', () => {
    const entry: ManpowerLogDto = {
      id: 'log-1',
      projectId: 'project-1',
      logDate: '2026-07-08T00:00:00.000Z',
      shift: 'Night',
      workCategoryId: VALID_GUID,
      wbsNodeId: null,
      activityId: null,
      labourType: 'Subcontract',
      subcontractorRef: 'ABC Construction',
      workerCount: 25,
      manHours: '200.00',
      overtimeHours: '10.00',
      manHoursDerived: true,
      equipmentCount: 2,
      equipmentOperatingHours: '12.00',
      equipmentStandbyHours: '4.00',
      workDescription: 'โครงสร้าง ชั้น 9',
      relatedWeatherLogId: null,
      recordedByUserId: 'user-1',
      recordedAt: '2026-07-08T09:00:00.000Z',
      entryKind: 'Original',
      correctsLogId: null,
      correctionReason: null,
      allowDuplicateOverride: false,
    }

    const values = manpowerLogFormValuesFromEntry(entry)
    expect(values.logDate).toBe('2026-07-08')
    expect(values.shift).toBe('Night')
    expect(values.labourType).toBe('Subcontract')
    expect(values.subcontractorRef).toBe('ABC Construction')
    expect(values.workerCount).toBe('25')
    expect(values.manHours).toBe('200.00')
    expect(values.manHoursDerived).toBe(true)
  })
})
