/**
 * 403 state shown when an authenticated user's role does not satisfy a route's required role(s)
 * (S2-FE-02 DoD: an actual 403 state, never a blank page or silent redirect-to-nothing). No real
 * Admin-only route exists yet — the first one is Sprint 9's tenant-admin approval-matrix UI — so
 * this page is proven against a fixture (see `RequireRole.test.tsx`) until then.
 */
export function ForbiddenPage() {
  return (
    <div className="flex min-h-screen items-center justify-center bg-bg px-4">
      <div
        role="alert"
        className="w-full max-w-sm rounded-card border border-border bg-surface p-8 text-center"
      >
        <p className="font-heading text-3xl font-bold text-danger">403</p>
        <h1 className="mt-2 text-lg font-semibold text-navy">ไม่มีสิทธิ์เข้าถึงหน้านี้</h1>
        <p className="mt-2 text-sm text-text-muted">
          บัญชีของคุณไม่มีบทบาทที่จำเป็นสำหรับหน้านี้ กรุณาติดต่อผู้ดูแลระบบหากคิดว่านี่คือความผิดพลาด
        </p>
      </div>
    </div>
  )
}
