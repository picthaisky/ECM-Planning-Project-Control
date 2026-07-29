import { ImportHistoryCard } from './ImportHistoryCard'
import { ImportUploadCard } from './ImportUploadCard'
import { useImportWizard } from './useImportWizard'

export interface ImportWizardProps {
  projectId: string
}

/**
 * S3-FE-01: the full import wizard (upload -> progress -> results/errors -> history) for the
 * "ข้อมูลโครงการ" screen's right-hand column — the `นำเข้าข้อมูลแผนงาน (Import)` and
 * `ประวัติการนำเข้าล่าสุด` cards from docs/ECM Planning Prototype.dc.html screen #2.
 *
 * Deliberately self-contained: it renders only these two stacked cards (`flex flex-col gap-4`,
 * matching the prototype's `gap:16px` right column), not the left-hand Project Master card or the
 * rest of the "ข้อมูลโครงการ" screen — that is Sprint 4 (S4-FE-02/03), which slots this component
 * into its own layout alongside the master-data card.
 */
export function ImportWizard({ projectId }: ImportWizardProps) {
  const {
    upload,
    startImport,
    resetUpload,
    setClientValidationError,
    history,
    historyState,
    historyError,
    refreshHistory,
    templateDownload,
    downloadTemplate,
  } = useImportWizard(projectId)

  return (
    <div className="flex flex-col gap-4">
      <ImportUploadCard
        upload={upload}
        onSelectFile={startImport}
        onClientValidationError={setClientValidationError}
        onReset={resetUpload}
        onDownloadTemplate={downloadTemplate}
        templateDownloadState={templateDownload.state}
        templateDownloadError={templateDownload.message}
      />
      <ImportHistoryCard
        jobs={history}
        state={historyState}
        errorMessage={historyError}
        onRetry={refreshHistory}
      />
    </div>
  )
}
