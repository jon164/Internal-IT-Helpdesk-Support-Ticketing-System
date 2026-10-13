import axe from 'axe-core'

/**
 * Runs axe-core's WCAG 2.0/2.1 A and AA rules against a rendered container.
 * Colour contrast is excluded because jsdom does not compute styles; it needs a real browser.
 */
export async function axeViolations(container: Element) {
  const results = await axe.run(container, {
    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'] },
    rules: { 'color-contrast': { enabled: false } },
  })
  return results.violations.map(v => `${v.id}: ${v.help} (${v.nodes.length} node(s))`)
}
