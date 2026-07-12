# Lessons Learned

Append-only, newest first. Written by knowledge-curator (or via `/learn`).
Every entry must end with an actionable rule. QA turns recurring lessons into permanent tests.

Entry format:

```
## YYYY-MM-DD — <short title>
Context: <task/feature>
What happened: <the failure or correction, 1–3 lines>
Root cause: <why>
Rule: <what every agent does differently next time>
```

---

## 2026-07-11 — Knowledge base initialized
Context: Multi-agent system bootstrap from docs/1–8.
What happened: Team, skills, and knowledge base created from the product documentation.
Root cause: —
Rule: Agents consult INDEX.md before non-trivial work; work that reveals a gap in this
knowledge base must end with a `/learn` capture so the gap closes permanently.

## 2026-07-12 — Design system was built from prose before a working prototype existed
Context: The team's first pass at `/cmplus-ui` and `CLAUDE.md` encoded a "warm orange-brown"
theme purely from `docs/3.`'s narrative description. The user then supplied a working HTML
prototype (`docs/ECM Planning Prototype.dc.html`) with a different, concrete navy/gold theme
and a 13-screen nav that doesn't exactly match `docs/6.`'s 15-module list.
What happened: Had the prototype been checked first, the initial design system would have
been correct on the first pass instead of needing a correction (ADR-0006).
Root cause: Text specs describe *intent*; a working prototype is *ground truth* and can
diverge from earlier prose as the product evolves. We designed from the older artifact only.
Rule: When both a prose doc and a working prototype/mockup exist for the same subject, always
open and defer to the prototype — treat prose docs as historical intent, not current spec.
When a new prototype/mockup file appears in the repo, proactively diff it against the current
design system and knowledge base before doing unrelated work.
