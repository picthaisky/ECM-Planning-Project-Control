// @ts-check
/**
 * Local ESLint rule: disallow raw hex color literals in .tsx/.jsx files.
 *
 * Per docs/10. §5 (P0-FE-02 DoD) and the `/cmplus-ui` skill, every color must
 * come from the theme tokens in `src/config/theme.ts` (or the matching
 * Tailwind utility class generated from them) — never a hardcoded hex value
 * inside a component. This rule is registered only against `**\/*.{tsx,jsx}`
 * in eslint.config.js, so `src/config/theme.ts` and `tailwind.config.ts`
 * (both `.ts`) are naturally exempt — that's where the tokens are defined.
 */

const HEX_COLOR_RE = /#(?:[0-9a-fA-F]{3,4}){1,2}\b/

/** @type {import('eslint').Rule.RuleModule} */
const noRawHexColors = {
  meta: {
    type: 'problem',
    docs: {
      description:
        'Disallow raw hex color literals in components — use a theme token from src/config/theme.ts or the matching Tailwind class instead.',
    },
    schema: [],
    messages: {
      rawHex:
        'Raw hex color "{{value}}" is not allowed here. Import a token from src/config/theme.ts or use the matching Tailwind utility class.',
    },
  },
  create(context) {
    /**
     * @param {import('estree').Node} node
     * @param {unknown} raw
     */
    function check(node, raw) {
      if (typeof raw === 'string' && HEX_COLOR_RE.test(raw)) {
        const match = HEX_COLOR_RE.exec(raw)
        context.report({
          node,
          messageId: 'rawHex',
          data: { value: match ? match[0] : raw },
        })
      }
    }

    return {
      Literal(node) {
        if (typeof node.value === 'string') check(node, node.value)
      },
      TemplateElement(node) {
        check(node, node.value.raw)
      },
    }
  },
}

export default noRawHexColors
