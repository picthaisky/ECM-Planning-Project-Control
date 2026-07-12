# CPM Method — Canonical Reference (with test fixtures)

Source: docs/4. §2 + docs/วิเคราะห์ฯ §3. Engine lives in the Application layer as a pure service.

## Algorithm

1. **Validate graph:** topological sort; a cycle → reject with the offending relation chain.
2. **Forward pass:** project start ES = 0. For activity $i$ (duration $D_i$):
   $EF_i = ES_i + D_i$; $ES_i = \max_{p \in Pred(i)} (\text{constraint}(p, i))$ where for
   relation type + lag $L$: FS: $EF_p + L$ · SS: $ES_p + L$ · FF: $FF$ constraint applies to
   finish ($EF_i \ge EF_p + L$) · SF: $EF_i \ge ES_p + L$.
3. **Backward pass:** $LF_{project} = \max EF$. $LS_i = LF_i - D_i$;
   $LF_i = \min_{s \in Succ(i)} (\text{constraint}(i, s))$ (FS: $LS_s - L$, etc.).
4. **Floats:** $TF_i = LS_i - ES_i$; $FF_i = \min_{s}(ES_s) - EF_i$ (FS relations).
5. **Critical:** $TF_i = 0$ → `IsCritical = true` → red bar (`amber-deep #A1523E`) on Gantt.

## Rules

- Work in calendar-aware working days when calendars exist (P6 semantics); durations in days (`Int`).
- Activities with no predecessor start at project start; no successor → constrain by project finish.
- FF/SF constraints on the finish must be converted consistently in both passes — this is the
  classic source of off-by-one defects; test each relation type separately.
- Data date: actualized activities (with ActualStart/ActualFinish) are fixed; remaining work
  schedules forward of the data date.
- Reconciliation: same network must produce identical dates/floats to Primavera P6
  (golden-file tests — project risk #2, Critical).

## Worked fixture (unit test verbatim)

Network (all FS, lag 0): A(5) → B(3) → D(4); A(5) → C(6) → D(4). Project start day 0.
- Forward: ES_A=0, EF_A=5; ES_B=5, EF_B=8; ES_C=5, EF_C=11; ES_D=11, EF_D=15.
- Backward: LF_D=15, LS_D=11; LF_C=11, LS_C=5; LF_B=11, LS_B=8; LF_A=5, LS_A=0.
- Floats: TF_A=0, TF_B=3, TF_C=0, TF_D=0 → Critical path **A → C → D**, duration 15.
- FF_B = ES_D − EF_B = 3.

Edge fixtures to always include: cycle (A→B→A) rejected; SS with lag 2; FF with lag 1;
isolated activity (no relations); duplicate relations between same pair (reject or dedupe per ADR).
