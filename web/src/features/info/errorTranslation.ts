/**
 * Thai-first translation for the import pipeline's error codes (S3-FE-01 DoD: "parser errors
 * displayed in Thai, with the specific line/activity that failed"). The backend's parser/handler
 * messages (`CMPlus.Application.Import.ImportErrorCodes`, `ImportErrorDetail`) are English
 * technical strings built for engineers reading logs — this module maps the stable *code* to a
 * Thai headline, and best-effort extracts the specific line/row/task/activity identifier the
 * parser already reported so it can be called out on its own, in Thai, rather than buried inside
 * an English sentence.
 */

/** Mirrors `CMPlus.Application.Import.ImportErrorCodes`'s constants exactly — keep in sync if the
 * backend adds a code. An unmapped code still gets a sensible generic title (see
 * `translateImportErrorCode`), never a blank/undefined one. */
const IMPORT_ERROR_TITLES: Record<string, string> = {
  ImportFileTooLarge: 'ไฟล์มีขนาดใหญ่เกินกำหนด',
  ImportMalformedFile: 'รูปแบบไฟล์ไม่ถูกต้อง',
  ImportRelationCycleDetected: 'พบการอ้างอิงกิจกรรมแบบวนซ้ำ (Cycle)',
  ImportXxeRejected: 'ไฟล์ XML มีการอ้างอิงภายนอกที่ไม่ปลอดภัย (XXE) จึงถูกปฏิเสธ',
  ImportUnknownActivity: 'พบรหัสกิจกรรมที่ไม่มีอยู่ในโครงการนี้',
  ImportProjectNotFound: 'ไม่พบโครงการที่ระบุ',
  ImportUnsupportedFormat: 'รูปแบบไฟล์ไม่รองรับ (ต้องเป็น .xer, .xml หรือ .xlsx)',
  ImportJobNotFound: 'ไม่พบประวัติการนำเข้านี้',
}

const DEFAULT_ERROR_TITLE = 'นำเข้าไฟล์ไม่สำเร็จ'

/** Translates a request-level (`ProblemDetails.detail`) or job-level error *code* to a Thai
 * headline. Falls back to a generic-but-honest message for any code not in the table above
 * (e.g. a validation error not specific to import) rather than surfacing raw English. */
export function translateImportErrorCode(code: string): string {
  return IMPORT_ERROR_TITLES[code] ?? DEFAULT_ERROR_TITLE
}

export interface ParsedImportError {
  code: string
  /** Thai headline for `code` (see `translateImportErrorCode`). */
  title: string
  /** The specific line/row/task/activity the parser named, labeled in Thai — `null` when the
   * message didn't name one (e.g. a generic "no TASK table" structural error). Identifiers
   * (task codes, UIDs, row numbers) are kept verbatim since they are not translatable content. */
  location: string | null
  /** The backend's own message, verbatim, for anyone who needs to cross-reference the exact
   * parser output (conventions.md: never hide the underlying reason). */
  detail: string
}

/** Tried in order against `detail`; each mirrors a message shape the real parsers emit today
 * (`XerDocumentReader`, `XerScheduleParser`, `MspdiScheduleParser`, `ExcelProgressImporter`) so the
 * identifier the parser already reports is pulled out and labeled in Thai, not re-derived. */
const LOCATION_PATTERNS: Array<[RegExp, (match: RegExpMatchArray) => string]> = [
  [/\bline (\d+)\b/i, (m) => `บรรทัดที่ ${m[1]}`],
  [/\brow\(s\)\s+([\d,\s]+?)\s+(?:reference|references|has)/i, (m) => `แถวที่ ${m[1].trim()}`],
  [/\brow (\d+)\b/i, (m) => `แถวที่ ${m[1]}`],
  [/\bTask UID (\d+)\b/i, (m) => `กิจกรรม Task UID ${m[1]}`],
  [/\bTASK '([^']+)'/i, (m) => `กิจกรรมรหัส ${m[1]}`],
  [/PROJWBS row '([^']+)'/i, (m) => `WBS รหัส ${m[1]}`],
  [/\bActivityId '([^']+)'/i, (m) => `รหัสกิจกรรม ${m[1]}`],
]

function extractLocation(code: string, detail: string): string | null {
  // The relation-cycle detail *is* the offending chain (e.g. "A-1010 -> A-1020 -> A-1010") with no
  // other prose around it — show the whole chain as the location rather than pattern-matching it.
  if (code === 'ImportRelationCycleDetected') {
    return `เส้นทางวนซ้ำ: ${detail.replace(/->/g, '→')}`
  }

  for (const [pattern, format] of LOCATION_PATTERNS) {
    const match = detail.match(pattern)
    if (match) return format(match)
  }

  return null
}

/**
 * Parses a `FileImportJob.errorJson` payload (`ImportErrorDetail.ToJson()` on the backend, shape
 * `{ code, detail }`) into a Thai-first title plus, where the parser named one, a `location`
 * string. Returns `null` for a falsy input (a job that hasn't failed has no error to parse).
 */
export function parseImportErrorJson(
  errorJson: string | null | undefined,
): ParsedImportError | null {
  if (!errorJson) return null

  let code = 'ImportUnknownError'
  let detail = errorJson

  try {
    // `ImportErrorDetail.ToJson()` (backend) serializes via a bare `JsonSerializer.Serialize`
    // with no `PropertyNamingPolicy` set — verified against the real API (S3-FE-01 end-to-end
    // check): it emits PascalCase (`"Code"`/`"Detail"`), unlike every *other* WebApi response
    // (which goes through Program.cs's project-wide camelCase `JsonSerializerOptions`). Accept
    // either casing so this keeps working if that inconsistency is ever fixed backend-side.
    const parsed = JSON.parse(errorJson) as {
      code?: string
      Code?: string
      detail?: string
      Detail?: string
    }
    const parsedCode = parsed.code ?? parsed.Code
    const parsedDetail = parsed.detail ?? parsed.Detail
    if (parsedCode) code = parsedCode
    if (parsedDetail) detail = parsedDetail
  } catch {
    // Not JSON — shouldn't happen given the backend's ImportErrorDetail.ToJson contract, but fall
    // back to showing the raw string as the detail under a generic title rather than throwing.
  }

  return {
    code,
    title: translateImportErrorCode(code),
    location: extractLocation(code, detail),
    detail,
  }
}
