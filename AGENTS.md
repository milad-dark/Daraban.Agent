# AGENTS.md

## Mandatory workflow rule

Before starting ANY new task or coding session:

1. `git checkout master`
2. `git pull`
3. `git checkout -b <task-name>` — create a new branch named after the task
4. Only then write code — never code directly on master.

Never start working on a task from a stale checkout or a non-master branch unless the user explicitly says otherwise.