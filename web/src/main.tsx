import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { ToastViewport } from './components'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
    {/* Cross-cutting (S2-FE-01): renders toasts pushed from anywhere, e.g. apiClient.ts's 401
        "session expired" notice, regardless of which screen is currently mounted. */}
    <ToastViewport />
  </StrictMode>,
)
