import type { Notice } from '../types'

export function NoticeBanner({ notice, onClose }: { notice: Notice; onClose: () => void }) {
  return (
    <div className={`notice ${notice.type}`} role="status" aria-live="polite">
      <strong>{notice.type === 'success' ? '✓' : notice.type === 'error' ? '!' : 'i'}</strong>
      <span>{notice.text}</span>
      <button type="button" onClick={onClose} aria-label="Close notification">×</button>
    </div>
  )
}
