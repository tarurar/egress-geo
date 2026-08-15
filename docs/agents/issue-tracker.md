# Issue tracker: GitHub

Issues and specs for this repository live as GitHub issues. Use the `gh` CLI
for all operations.

## Conventions

- **Create an issue:** `gh issue create --title "..." --body "..."`.
- **Read an issue:** `gh issue view <number> --comments`.
- **List issues:** use `gh issue list` with appropriate label and state
  filters.
- **Comment:** `gh issue comment <number> --body "..."`.
- **Apply or remove labels:** use `gh issue edit` with `--add-label` or
  `--remove-label`.
- **Close:** `gh issue close <number> --comment "..."`.

Infer the repository from `git remote -v`; `gh` does this automatically when
run inside the checkout.

## Pull requests as a triage surface

**PRs as a request surface: no.**

GitHub shares one number space across issues and pull requests. Resolve an
ambiguous reference with `gh pr view <number>` and fall back to
`gh issue view <number>`.

## Skill operations

When a skill says to publish to the issue tracker, create a GitHub issue. When
a skill says to fetch the relevant ticket, run
`gh issue view <number> --comments`.

## Wayfinding operations

- A map is one issue labelled `wayfinder:map`.
- Child tickets use GitHub sub-issues when available. Otherwise, list them in
  the map body and add `Part of #<map>` to each child.
- Represent blocking with GitHub issue dependencies when available. Otherwise,
  add a `Blocked by: #<number>` line to the child.
- Claim a ticket with `gh issue edit <number> --add-assignee @me`.
- Resolve a ticket by commenting with the answer and then closing it.
