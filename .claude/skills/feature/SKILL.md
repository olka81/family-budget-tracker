---
name: feature
description: Implement a feature from a spec file end to end — branch, plan for approval, implementation, build, independent verification, commit, push. Run only when Olga invokes /feature <path-to-spec>.
argument-hint: [path-to-spec.md]
disable-model-invocation: true
---

Implement the feature described in the spec file: $ARGUMENTS

Write all messages to Olga, the plan and the report in English. Code comments stay in English.

Sound signal: every time you stop and wait for Olga's input (the plan, any STOP, a question) and when you finish (after the report), run as your LAST action, verbatim:
`powershell.exe -NoProfile -Command "[console]::beep(880,300)"`
This exact command is allowed in Olga's local settings; do not change it.

## 0. Read the spec

Read the spec file. It must contain these sections: **Branch**, **Goal**, **Requirements**, **Acceptance criteria**, **Out of scope**.
If the file does not exist or any section is missing, STOP and report what is missing. Do not guess.

## 1. Preflight and branch

1. `git status` — if the working tree is not clean, STOP and report.
2. Check whether the branch from the spec already exists, locally or on `origin` (`git branch --list <branch>`, `git ls-remote --heads origin <branch>`).
3. **New branch:** `git checkout main`, `git pull`, `git checkout -b <branch>`.
4. **Existing branch** (continuing unfinished work): `git checkout <branch>`, and `git pull` if it tracks `origin`. Do NOT reset, rebase or recreate it. Check whether it is behind `main` (`git log --oneline HEAD..main`); if it is, report this in the plan and ask whether to merge `main` — do not merge on your own.

## 2. Study

Read `CLAUDE.md` (especially the Architecture Decisions), then the parts of the spec documents (TZ, DB_SCHEMA, PLAN) and the code that the feature touches.
On an existing branch, also study what is already done there: `git log --oneline main..HEAD` and `git diff main...HEAD`.

## 3. Plan — STOP and wait for approval

Show Olga a plan:
- on an existing branch: what is already done there and what is left;
- files to create and to modify, and what changes in each;
- whether the database schema changes (a migration is needed) — yes or no, and why;
- which automated tests will be added or changed in `tests/FamilyBudget.Tests`, listed by name (see step 4); if you believe the change needs no tests, say why;
- how each acceptance criterion will be met;
- open questions and anything ambiguous in the spec.

Do NOT write any code until Olga answers "ок". If she asks for changes, revise the plan and wait again.

## 4. Implement

- Implement exactly the approved plan, following the Architecture Decisions in `CLAUDE.md`.
- If during the work the plan has to change, or a schema change turns out to be needed that was not in the plan — STOP and ask.
- Tests: new or changed business logic (services, validation, calculations, data rules) comes with automated tests in `tests/FamilyBudget.Tests`, in the same commit. Cover the main path and every expected outcome the code returns. Razor/Blazor markup, styling, documentation and configuration do not need tests. Follow the existing test conventions there (shared `PostgresFixture`, each test creates its own data).
- Never weaken, skip or delete an existing test to make the suite pass. If an existing test fails because the behaviour was changed on purpose by the approved plan, update the test and say so in the report; if it fails for any other reason, STOP and report.
- Migrations: never run `dotnet ef migrations add`, `migrations remove` or `database update` without Olga's explicit approval in this session. Never apply anything to Neon.

## 5. Build

`dotnet build` must succeed with 0 errors, and `dotnet test tests/FamilyBudget.Tests` must pass with 0 failed tests. The tests need Docker running (Testcontainers); if Docker is not running, STOP and ask Olga to start it.
If you cannot make it pass within the approved plan, STOP and report.

## 6. Stage

Stage only the files from the approved plan. Run `git status --porcelain --untracked-files=all` and make sure nothing unexpected is changed or left untracked.

## 7. Verify

Before committing, use the `verifier` subagent. Pass it:
- the FULL text of the spec file VERBATIM, as the ORIGINAL TASK;
- the approved plan VERBATIM, labelled "APPROVED PLAN".

If the verdict is FAIL: do not commit. Report the discrepancies to Olga and STOP.

## 8. Commit and push

If PASS:
1. Commit with a message `<prefix>: <short summary in English>`. Prefix: `feat` for a feature, `fix` for a bug fix, or the prefix the spec names (`test`, `chore`, `docs`).
2. Check that the commit succeeded (`git log -1` shows the new commit) before pushing. Then `git push -u origin <branch>`.
   Never put commit and push in one command joined with `;` — if the commit fails, the push still runs. Run them as separate commands, or join them with `&&`. If the commit fails, do not push: fix the cause or STOP and report.
3. Do NOT open a PR.

## 9. Report

Report in English, and also save the full report to `D:\Cowork\PetProject\report.md`:
- branch and commit hash;
- `git show --stat HEAD`;
- build result and test result (passed / failed / skipped counts), and the list of tests added or changed;
- the verifier's answer in full;
- whether a migration is needed and its status;
- how Olga can check the feature by hand (steps to run locally).
