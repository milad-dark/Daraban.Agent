# AGENTS.md

Work like a contractor who bills for rework: the cost of a wrong assumption is yours to avoid, and the cost of an unnecessary question is mine to pay.

## Mandatory workflow rule

Before starting ANY new task or coding session:

1. `git checkout master`
2. `git pull`
3. `git checkout -b <task-name>` — create a new branch named after the task
4. Only then write code — never code directly on master.

Never start working on a task from a stale checkout or a non-master branch unless the user explicitly says otherwise.

## When this applies (proportionality)

This ceremony scales with blast radius.

- **Just do it** — typo fixes, renames, or changes under ~20 lines with one obvious correct form. No ceremony.
- **Full treatment** — a new module, a schema change, or anything touching auth, money, migrations, or deletion. Follow every step below, and be more suspicious than usual of your own assumptions.

## 1. Investigate before you ask

Read the relevant code, tests, configs, and dependency manifests first. Anything discoverable in under a minute of searching is not a question — it's research you owe me.

Never ask about the test framework, language version, lint rules, error-handling conventions, directory layout, or existing abstractions that already exist in the repo.

If the codebase contradicts itself, that's worth raising.

## 2. Then produce this, and stop

Before writing any code, produce the four sections below, then wait.

### Goal
One paragraph restating what I asked for in your own words, including the acceptance criteria you'll hold yourself to. If your restatement is wrong, that's the cheapest possible place to find out.

### Blocking questions (0–3)
Only ask when a wrong answer means throwing work away, not adjusting it. Each question gets a recommended default so I can reply "yes to all" — never ask an open question where a proposed answer would do. If nothing is genuinely blocking, say so and list zero.

### Assumptions
Numbered, specific, falsifiable. "Inputs are under 10k rows and fit in memory" is an assumption. "The code should be maintainable" is not. Cover whichever of these the task actually touches:

- **Data:** shape, volume, trust level, encoding, what a malformed input looks like
- **Failure:** what should happen on timeout, partial write, or downstream 500 — retry, fail loud, or degrade
- **Boundaries:** who calls this, what's public API vs. internal, backwards-compat obligations
- **State:** concurrency, idempotency, transactionality, ordering guarantees
- **Environment:** runtime version, where it deploys, what it's allowed to reach
- **Scope:** what you're deliberately *not* doing, and what you're leaving as TODO
- **Testing:** what you'll write tests for and what you'll leave uncovered

### Plan
Files you'll create or modify, the key function/type signatures, and the order you'll work in. Where you chose between real alternatives, name the alternative and say why you rejected it in one clause.

**Then wait. Do not begin implementing.**