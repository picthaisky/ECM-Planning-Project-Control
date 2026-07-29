import { describe, expect, it } from 'vitest'
import { parseImportErrorJson, translateImportErrorCode } from './errorTranslation'

function toErrorJson(code: string, detail: string): string {
  return JSON.stringify({ code, detail })
}

describe('translateImportErrorCode', () => {
  it('translates every known ImportErrorCodes constant to a Thai title', () => {
    expect(translateImportErrorCode('ImportFileTooLarge')).toBe('ไฟล์มีขนาดใหญ่เกินกำหนด')
    expect(translateImportErrorCode('ImportMalformedFile')).toBe('รูปแบบไฟล์ไม่ถูกต้อง')
    expect(translateImportErrorCode('ImportRelationCycleDetected')).toContain('วนซ้ำ')
    expect(translateImportErrorCode('ImportXxeRejected')).toContain('XXE')
    expect(translateImportErrorCode('ImportUnknownActivity')).toContain('กิจกรรม')
    expect(translateImportErrorCode('ImportProjectNotFound')).toBe('ไม่พบโครงการที่ระบุ')
    expect(translateImportErrorCode('ImportUnsupportedFormat')).toContain('.xlsx')
    expect(translateImportErrorCode('ImportJobNotFound')).toBe('ไม่พบประวัติการนำเข้านี้')
  })

  it('falls back to a generic Thai message for an unmapped code', () => {
    expect(translateImportErrorCode('SomethingBackendAddedLater')).toBe('นำเข้าไฟล์ไม่สำเร็จ')
  })
})

describe('parseImportErrorJson', () => {
  it('returns null for a falsy input (job has not failed)', () => {
    expect(parseImportErrorJson(null)).toBeNull()
    expect(parseImportErrorJson(undefined)).toBeNull()
    expect(parseImportErrorJson('')).toBeNull()
  })

  it('extracts a line number from XerDocumentReader-shaped messages', () => {
    const result = parseImportErrorJson(
      toErrorJson('ImportMalformedFile', 'line 42 is a %T record with no table name.'),
    )
    expect(result?.title).toBe('รูปแบบไฟล์ไม่ถูกต้อง')
    expect(result?.location).toBe('บรรทัดที่ 42')
    expect(result?.detail).toBe('line 42 is a %T record with no table name.')
  })

  it('extracts a plural row list from ExcelProgressImporter/UnknownActivity-shaped messages', () => {
    const result = parseImportErrorJson(
      toErrorJson(
        'ImportUnknownActivity',
        'Row(s) 3, 5 reference an ActivityId that does not belong to this project.',
      ),
    )
    expect(result?.title).toContain('กิจกรรม')
    expect(result?.location).toBe('แถวที่ 3, 5')
  })

  it('extracts a single row number from ExcelProgressImporter-shaped messages', () => {
    const result = parseImportErrorJson(
      toErrorJson('ImportMalformedFile', "row 7 has an invalid ActivityId '123'."),
    )
    expect(result?.location).toBe('แถวที่ 7')
  })

  it('extracts a TASK code from XerScheduleParser-shaped messages', () => {
    const result = parseImportErrorJson(
      toErrorJson(
        'ImportMalformedFile',
        "TASK 'A1010' has an unparsable target_start_date/target_end_date.",
      ),
    )
    expect(result?.location).toBe('กิจกรรมรหัส A1010')
  })

  it('extracts a Task UID from MspdiScheduleParser-shaped messages', () => {
    const result = parseImportErrorJson(
      toErrorJson('ImportMalformedFile', 'Task UID 55 has an unparsable Start/Finish.'),
    )
    expect(result?.location).toBe('กิจกรรม Task UID 55')
  })

  it('extracts a WBS code from PROJWBS-shaped messages', () => {
    const result = parseImportErrorJson(
      toErrorJson(
        'ImportMalformedFile',
        "PROJWBS row 'WBS-99' is missing wbs_short_name/wbs_name.",
      ),
    )
    expect(result?.location).toBe('WBS รหัส WBS-99')
  })

  it('shows the full relation cycle chain as the location, with arrows for readability', () => {
    const result = parseImportErrorJson(
      toErrorJson('ImportRelationCycleDetected', 'A-1010 -> A-1020 -> A-1030 -> A-1010'),
    )
    expect(result?.title).toBe('พบการอ้างอิงกิจกรรมแบบวนซ้ำ (Cycle)')
    expect(result?.location).toBe('เส้นทางวนซ้ำ: A-1010 → A-1020 → A-1030 → A-1010')
  })

  it('parses the real backend wire shape (PascalCase "Code"/"Detail" from ImportErrorDetail.ToJson, confirmed against the live API)', () => {
    const result = parseImportErrorJson(
      '{"Code":"ImportRelationCycleDetected","Detail":"A1010 -> A1020 -> A1030 -> A1010"}',
    )
    expect(result?.title).toBe('พบการอ้างอิงกิจกรรมแบบวนซ้ำ (Cycle)')
    expect(result?.location).toBe('เส้นทางวนซ้ำ: A1010 → A1020 → A1030 → A1010')
  })

  it('returns a null location when the message names no specific line/row/task', () => {
    const result = parseImportErrorJson(
      toErrorJson('ImportMalformedFile', 'the file contains no TASK table.'),
    )
    expect(result?.location).toBeNull()
    expect(result?.detail).toBe('the file contains no TASK table.')
  })

  it('falls back to a generic title and the raw string as detail when errorJson is not valid JSON', () => {
    const result = parseImportErrorJson('not json at all')
    expect(result?.title).toBe('นำเข้าไฟล์ไม่สำเร็จ')
    expect(result?.detail).toBe('not json at all')
  })
})
