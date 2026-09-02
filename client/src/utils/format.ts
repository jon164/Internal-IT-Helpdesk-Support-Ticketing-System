import type { StatTone } from '../components/StatTile'

/**
 * The attainment level the desk holds itself to. Kept here as a named constant rather than scattered
 * through comparisons, so changing the bar the dashboard judges against is a one-line edit.
 */
export const ATTAINMENT_TARGET_PERCENT = 90

/**
 * Elapsed time, in the units a person actually thinks in.
 *
 * Note the unit is minutes *of the ticket's own SLA clock* — continuous for P1 and P2, working time
 * for P3 and P4 — so this is a duration against target, not wall-clock age. The dashboard says so
 * beside every figure that uses it, because the two differ by a lot over a weekend.
 */
export function formatMinutes(minutes: number | null): string {
  if (minutes === null) {
    return '—'
  }

  const total = Math.round(minutes)

  if (total < 60) {
    return `${total} min`
  }

  const hours = Math.floor(total / 60)
  const remainingMinutes = total % 60

  if (hours < 24) {
    return remainingMinutes === 0 ? `${hours}h` : `${hours}h ${remainingMinutes}m`
  }

  const days = Math.floor(hours / 24)
  const remainingHours = hours % 24

  return remainingHours === 0 ? `${days}d` : `${days}d ${remainingHours}h`
}

export function formatPercent(value: number | null, fractionDigits = 1): string {
  return value === null ? '—' : `${value.toFixed(fractionDigits)}%`
}

/**
 * Maps an attainment figure onto a status band.
 *
 * A null figure is neutral, never bad: "nothing was due this period" must not render as a failure.
 */
export function attainmentTone(percent: number | null): StatTone {
  if (percent === null) {
    return 'neutral'
  }

  if (percent >= ATTAINMENT_TARGET_PERCENT) {
    return 'good'
  }

  if (percent >= ATTAINMENT_TARGET_PERCENT - 10) {
    return 'warning'
  }

  return 'critical'
}

/** Breaches are binary: any open breach is worth the manager's attention. */
export function breachTone(count: number): StatTone {
  if (count === 0) {
    return 'good'
  }

  return count <= 3 ? 'warning' : 'critical'
}

/** Turns a status enum name into something readable, e.g. InProgress -> In progress. */
export function humaniseStatus(status: string): string {
  const spaced = status.replace(/([a-z])([A-Z])/g, '$1 $2')
  return spaced.charAt(0) + spaced.slice(1).toLowerCase()
}

/**
 * An age in days, at a precision that matches how long the thing has been waiting.
 *
 * Something four hours old is reported in hours; something 148 days old is not reported as
 * "148.4 days", because the fraction is noise at that scale.
 */
export function formatDays(days: number | null): string {
  if (days === null) {
    return '—'
  }

  if (days < 1) {
    const hours = Math.round(days * 24)
    return hours <= 1 ? 'under an hour' : `${hours} hours`
  }

  if (days < 10) {
    return `${days.toFixed(1)} days`
  }

  return `${Math.round(days)} days`
}

/** Short axis label for a week, e.g. "4 Aug". */
export function formatShortDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, { day: 'numeric', month: 'short' })
}

export function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
  })
}

/** Date and time, for an audit entry where the hour matters. */
export function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

/**
 * How long is left against a target, or how far past it the ticket has gone.
 *
 * Negative remaining minutes are reported as "overdue by", not as a minus sign. A technician
 * scanning a queue should not have to work out what "-431 min" means, and the word carries the
 * meaning for anyone who cannot distinguish the colour that accompanies it.
 */
export function formatRemaining(minutes: number): string {
  return minutes < 0
    ? `overdue by ${formatMinutes(Math.abs(minutes))}`
    : `${formatMinutes(minutes)} left`
}
