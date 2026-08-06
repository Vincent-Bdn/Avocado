import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { App } from './App.js'
import { applyTheme } from './theme.js'
// theme.css pulls in Tailwind, the fonts and tokens.css; every screen is styled with utilities from
// there. print.css is the one exception: an A4 page has its own measurements and its own rules.
import './theme.css'
import './print.css'

applyTheme()

const root = document.getElementById('root')
if (!root) {
  throw new Error('#root introuvable')
}

createRoot(root).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
