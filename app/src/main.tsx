import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { App } from './App.js'
import { applyTheme } from './theme.js'
import './tokens.css'
import './app.css'
import './shell.css'

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
