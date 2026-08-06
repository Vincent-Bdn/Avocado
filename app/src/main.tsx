import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { App } from './App.js'
import { applyTheme } from './theme.js'
// theme.css pulls in Tailwind, the fonts and tokens.css. The three below are the not-yet-converted
// screens; each disappears as its handoff is worked through.
import './theme.css'
import './app.css'
import './shell.css'
import './wizard/wizard.css'

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
