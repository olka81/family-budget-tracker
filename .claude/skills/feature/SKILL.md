---
name: feature
description: Implement a feature from a spec file end to end — branch, plan for approval, implementation, build, independent verification, commit, push. Run only when Olga invokes /feature <path-to-spec>.
argument-hint: [path-to-spec.md]
disable-model-invocation: true
---

Implement the feature described in the spec file: $ARGUMENTS

Write all messages to Olga, the plan and the report in Russian. Code comments stay in English.

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
- how each acceptance criterion will be met;
- open questions and anything ambiguous in the spec.

Do NOT write any code until Olga answers "ок". If she asks for changes, revise the plan and wait again.

## 4. Implement

- Implement exactly the approved plan, following the Architecture Decisions in `CLAUDE.md`.
- If during the work the plan has to change, or a schema change turns out to be needed that was not in the plan — STOP and ask.
- Migrations: never run `dotnet ef migrations add`, `migrations remove` or `database update` without Olga's explicit approval in this session. Never apply anything to Neon.

## 5. Build

`dotnet build` must succeed with 0 errors. If a test project exists, `dotnet test` must pass.
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
1. Commit with a message `feat: <short summary in English>`.
2. `git push -u origin <branch>`.
3. Do NOT open a PR.

## 9. Report

Report in Russian, and also save the full report to `D:\Cowork\PetProject\report.md`:
- branch and commit hash;
- `git show --stat HEAD`;
- build (and test) result;
- the verifier's answer in full;
- whether a migration is needed and its status;
- how Olga can check the feature by hand (steps to run locally).
