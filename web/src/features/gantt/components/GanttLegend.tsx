/**
 * Bar-color legend, transcribed from the prototype's Gantt header
 * (`docs/ECM Planning Prototype.dc.html` lines 208-212): critical (danger/red), non-critical
 * (secondary/slate-blue), baseline (gold). Tailwind utility classes only (`bg-danger`/`bg-secondary`/
 * `bg-gold`, generated from `src/config/theme.ts` — never a raw hex literal, enforced by the
 * `local/no-raw-hex-colors` eslint rule).
 *
 * The "Baseline" swatch is kept even though no baseline bar renders anywhere yet this sprint — no
 * `Baseline` entity/endpoint exists until Sprint 14 (see `components/GanttChart.tsx`'s remarks) —
 * so the legend still visually matches the prototype exactly (S6-QA-02's visual-regression target),
 * it simply has nothing to point at under any row today. Marked muted/dashed here to say so
 * honestly rather than implying baseline data exists.
 */
export function GanttLegend() {
  return (
    <div className="ml-3.5 flex items-center gap-3.5 text-[10.5px] text-text-faint">
      <span className="flex items-center gap-1.5">
        <span aria-hidden="true" className="inline-block h-2 w-3.5 rounded-sm bg-danger" />
        Critical (TF=0)
      </span>
      <span className="flex items-center gap-1.5">
        <span aria-hidden="true" className="inline-block h-2 w-3.5 rounded-sm bg-secondary" />
        Non-critical
      </span>
      <span className="flex items-center gap-1.5" title="ยังไม่มีข้อมูล Baseline (Sprint 14)">
        <span aria-hidden="true" className="inline-block h-1 w-3.5 rounded-sm border border-dashed border-gold" />
        Baseline
      </span>
    </div>
  )
}
