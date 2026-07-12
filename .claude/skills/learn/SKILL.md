---
name: learn
description: Capture lessons, decisions, and patterns from the current session into the team knowledge base (.claude/knowledge/) via the knowledge-curator agent. Invoke as /learn, optionally with a note about what to capture, e.g. /learn บทเรียนจากบั๊ก CPM lag.
---

# /learn — Capture Team Knowledge Now

Trigger the self-learning loop on demand.

## Steps

1. **Summarize the learnable events** from this session (and the argument, if given):
   - failures and their root causes; corrections the human made
   - decisions taken (and rejected alternatives)
   - patterns/approaches that worked well
   - anything an agent had to figure out that the knowledge base should have known
2. **Invoke the `knowledge-curator` agent** with that summary. It will classify each item
   (lesson / ADR / pattern / domain fact), write it into `.claude/knowledge/`, promote
   recurring patterns into skills, and prune stale entries per its own instructions.
3. **Relay the curator's report** to the user: exactly what was added, changed, promoted,
   or pruned.

If there is genuinely nothing worth capturing, say so — do not write noise into the
knowledge base.
