import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { Shell } from './components/Shell'
import { ProofPage } from './pages/ProofPage'
import { ArenaPage } from './pages/ArenaPage'
import { TourPage } from './pages/TourPage'
import { NotFoundPage } from './pages/NotFoundPage'

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route element={<Shell />}>
          <Route index element={<Navigate to="/proof" replace />} />
          <Route path="proof" element={<ProofPage />} />
          <Route path="arena" element={<ArenaPage />} />
          <Route path="tour" element={<TourPage />} />
          <Route path="*" element={<NotFoundPage />} />
        </Route>
      </Routes>
    </BrowserRouter>
  )
}
