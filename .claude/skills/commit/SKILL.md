---
name: commit
description: Inspect current changes and propose a Conventional Commit message.
disable-model-invocation: true
---
# Commit

Inspect `git status`, `git diff` and `git diff --staged`.

Understand what actually changed and propose one Conventional Commit:

`type(scope): description`

Use feat, fix, refactor, test, docs, chore, perf, build or ci.

Do not create the commit unless explicitly asked. If unrelated changes are mixed together, point that out.
