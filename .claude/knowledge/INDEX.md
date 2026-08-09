# CM+ Team Knowledge Base — Index

Persistent, compounding knowledge for the AI team. Every agent reads this index before
non-trivial work and follows links relevant to its task. Maintained by `knowledge-curator`
(manually triggerable via `/learn`).

## Domain (construction project control)

- [EVM formulas & worked examples](domain/evm-formulas.md) — PV/EV/AC, SV/CV, SPI/CPI, EAC/ETC/VAC, rollup rules, test fixtures
- [Actual Cost (AC/ACWP)](domain/actual-cost.md) — what AC is (incurred, accrual basis), granularity, data sources, why certified payments are never AC, bitemporal/append-only rules, `ActualCostEntry` shape, AC-1…AC-10 fixtures
- [CPM method](domain/cpm-method.md) — forward/backward pass, float, relation types, P6 reconciliation notes
- [Approval workflow](domain/approval-workflow.md) — VO & Payment Certificate state machines, per-tenant permission matrix, amount-tiered authority, routing fixtures
- [Payment & retention](domain/payment-retention.md) — corrected Net Payment formula, retention cap, advance-recovery methods, ledger, P1–P7 fixtures
- [The 15 modules map](domain/modules-map.md) — what each module does and which docs/spec covers it

## Architecture

- [Architecture Decision Records (ADR)](architecture/decisions.md) — accepted decisions; never contradict without superseding
- [Tech stack reference](architecture/tech-stack.md) — versions, libraries, and why they were chosen

## Engineering practice

- [Coding patterns & conventions](patterns/conventions.md) — reusable patterns promoted from real work
- [Lessons learned](lessons/lessons-learned.md) — failures, root causes, and the rules that prevent repeats

## Maintenance rules

- One fact lives in one place; link, don't duplicate.
- Entries are actionable rules, not narration. Keep them short.
- Stale knowledge is corrected or marked superseded on sight.
- New files must be registered in this index.
