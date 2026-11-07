import { Link } from 'react-router-dom'

export function NotFoundPage() {
  return (
    <section className="proof-error">
      <span className="label">404</span>
      <h1 className="display-l">There is no evidence at this address.</h1>
      <p className="prose">The three sections of the lab are proof, arena and tour.</p>
      <Link className="btn" to="/proof">
        Go to the proof
      </Link>
    </section>
  )
}
