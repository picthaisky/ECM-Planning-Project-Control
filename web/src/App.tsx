import { BrowserRouter } from 'react-router-dom'
import { AppRoutes } from './routes'

/**
 * App root (S4-FE-01, US-4.2). Replaces the Phase 0 scaffold placeholder now that the real app
 * shell + 13-screen nav exist (`components/layout/`, `routes/AppRoutes.tsx`).
 */
function App() {
  return (
    <BrowserRouter>
      <AppRoutes />
    </BrowserRouter>
  )
}

export default App
