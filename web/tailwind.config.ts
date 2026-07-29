import type { Config } from 'tailwindcss'
import { colors, fontFamily, radius } from './src/config/theme.ts'

// Tailwind theme is generated from `src/config/theme.ts` (the single source of
// truth) so components never need to hardcode hex values — use the utility
// classes below (e.g. `bg-navy`, `text-danger`, `font-heading`).
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        navy: colors.navy,
        gold: colors.gold,
        bg: colors.bg,
        surface: {
          DEFAULT: colors.surface,
          muted: colors.tableHeaderBg,
        },
        border: {
          DEFAULT: colors.border,
          subtle: colors.borderSubtle,
        },
        text: {
          DEFAULT: colors.text,
          muted: colors.textMuted,
          faint: colors.textFaint,
        },
        success: colors.success,
        danger: colors.danger,
        secondary: colors.secondary,
        warning: {
          text: colors.warningText,
        },
      },
      fontFamily: {
        sans: [...fontFamily.body],
        heading: [...fontFamily.heading],
      },
      borderRadius: {
        card: radius.card,
      },
    },
  },
  plugins: [],
} satisfies Config
