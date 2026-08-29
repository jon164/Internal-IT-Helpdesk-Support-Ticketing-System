import { describe, expect, it } from 'vitest'
import { attainmentTone, breachTone, formatMinutes, formatPercent, humaniseStatus } from './format'

describe('formatMinutes', () => {
  it('shows minutes below an hour', () => {
    expect(formatMinutes(45)).toBe('45 min')
  })

  it('shows hours and minutes below a day', () => {
    expect(formatMinutes(200)).toBe('3h 20m')
  })

  it('drops the minutes component when it is zero', () => {
    expect(formatMinutes(240)).toBe('4h')
  })

  it('shows days and hours beyond a day', () => {
    expect(formatMinutes(1800)).toBe('1d 6h')
  })

  it('renders a dash for no data rather than zero', () => {
    // The distinction matters: "nothing qualified" must never be displayed as "0 minutes".
    expect(formatMinutes(null)).toBe('—')
  })
})

describe('formatPercent', () => {
  it('formats to one decimal by default', () => {
    expect(formatPercent(84.27)).toBe('84.3%')
  })

  it('renders a dash for no data', () => {
    expect(formatPercent(null)).toBe('—')
  })
})

describe('attainmentTone', () => {
  it('treats no data as neutral, not as a failure', () => {
    expect(attainmentTone(null)).toBe('neutral')
  })

  it('marks attainment at or above the target as good', () => {
    expect(attainmentTone(90)).toBe('good')
    expect(attainmentTone(97.4)).toBe('good')
  })

  it('marks the ten points below target as a warning', () => {
    expect(attainmentTone(85)).toBe('warning')
  })

  it('marks anything lower as critical', () => {
    expect(attainmentTone(62)).toBe('critical')
  })
})

describe('breachTone', () => {
  it('reports no open breaches as good', () => {
    expect(breachTone(0)).toBe('good')
  })

  it('escalates as breaches accumulate', () => {
    expect(breachTone(2)).toBe('warning')
    expect(breachTone(9)).toBe('critical')
  })
})

describe('humaniseStatus', () => {
  it('splits the enum name into words', () => {
    expect(humaniseStatus('InProgress')).toBe('In progress')
    expect(humaniseStatus('OnHold')).toBe('On hold')
  })

  it('leaves a single word alone apart from casing', () => {
    expect(humaniseStatus('Triaged')).toBe('Triaged')
  })
})
