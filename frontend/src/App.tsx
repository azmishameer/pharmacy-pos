import { useEffect, useState } from 'react'

function App() {
  const [status, setStatus] = useState('Checking backend…')

  useEffect(() => {
    const controller = new AbortController()
    const timeout = window.setTimeout(() => controller.abort(), 5000)
    let active = true

    async function checkBackend() {
      try {
        const response = await fetch('/api/status', { signal: controller.signal, cache: 'no-store' })
        if (!response.ok) throw new Error('Status request failed')
        const data = await response.json()
        if (data.status !== 'ok') throw new Error('Unexpected status response')
        if (active) setStatus('Backend connected')
      } catch {
        if (active) setStatus('Cannot reach the backend. Check that it is running, then refresh this page.')
      } finally {
        window.clearTimeout(timeout)
      }
    }

    void checkBackend()
    return () => {
      active = false
      window.clearTimeout(timeout)
      controller.abort()
    }
  }, [])

  return (
    <main>
      <h1>Pharmacy POS</h1>
      <p>Welcome to your pharmacy sales and inventory system.</p>
      <p role="status">{status}</p>
    </main>
  )
}

export default App
