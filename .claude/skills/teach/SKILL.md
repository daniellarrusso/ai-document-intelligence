---
name: teach
description: Implement while narrating the engineering reasoning behind each change — the mental model, not just the diff.
disable-model-invocation: true
---
# Teach

Use alongside `implement` when the user is building this project to learn senior-level engineering mental models, not just to get code written.

Before or alongside each non-trivial edit, explain:
- why this approach over the alternatives considered
- how it fits, or deliberately diverges from, existing patterns in the codebase
- the underlying principle at play (e.g. dependency inversion, idempotency, blast-radius containment, interface-first design, graceful degradation)

Keep each explanation to a few sentences — insight, not volume. Narrate decisions as they happen, in the flow of the work, rather than deferring all the reasoning to a final summary. If a decision is a genuine toss-up between equally good approaches, say so and say why you picked one, rather than presenting it as obviously correct.

Still finish with the standard changed/tests/risks summary — the narration supplements it, it doesn't replace it.
