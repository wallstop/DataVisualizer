---
name: manage-skills
description: Create, edit, split, or remove agent skills in .llm/skills using the standard SKILL.md format (agentskills.io). Use when adding a new skill, updating any skill's frontmatter or body, regenerating the skills index, or when a .llm markdown file approaches the 300-line limit.
metadata:
  category: Core
---

# Skill: Manage Skills

Skills are standard [Agent Skills](https://agentskills.io): one directory per skill
under `.llm/skills/<skill-name>/` containing a `SKILL.md` with YAML frontmatter.

## Frontmatter Contract

```yaml
---
name: skill-name
description: What the skill does and when to use it, with searchable keywords.
metadata:
  category: Core
---
```

- `name` (required): lowercase letters, numbers, hyphens; no leading, trailing, or
  consecutive hyphens; max 64 characters; must equal the directory name.
- `description` (required): 1-1024 characters; state what the skill does AND when to
  use it; include the keywords an agent would search for.
- `metadata.category` (optional): one of `Core`, `Workflow`, `Feature` (defaults to
  `Feature`). `Core` = always consider; `Workflow` = process/procedure; `Feature` =
  domain-specific how-to.
- Optional spec keys (`license`, `compatibility`, `allowed-tools`, `metadata.*`) are
  tolerated but rarely needed.

## Body Contract

Keep bodies lean (aim under 120 lines, hard limit 300 for every `.md` under `.llm/`):

- Start with an H1 title matching the skill.
- Step-by-step instructions, concrete examples, and edge cases.
- Inline code examples stay under 20 lines; longer material goes to
  `.llm/code-samples/` and is linked relatively from the skill root.
- End with a `## Related Skills` section using relative links
  (`./other-skill/SKILL.md`).
- No duplication across skills: link instead of restating.

## Creating a Skill

1. Create `.llm/skills/<kebab-name>/SKILL.md` with valid frontmatter and a lean body.
2. Regenerate the index: `pwsh -NoProfile -File scripts/generate-skills-index.ps1`.
3. Validate: `pwsh -NoProfile -File scripts/lint-llm-instructions.ps1` and
   `pwsh -NoProfile -File scripts/lint-file-lengths.ps1` (or just `npm run lint:llm`).
4. Run the harness self-tests once before committing:
   `pwsh -NoProfile -File scripts/tests/run-all.ps1`.
5. Commit the new directory together with the regenerated `.llm/skills/index.md`.

## Editing a Skill

Run the size linter immediately after each edit
(`pwsh -NoProfile -File scripts/lint-file-lengths.ps1 -Paths .llm/skills/<name>/SKILL.md`):
size issues discovered at commit time require human judgment to fix.

## Splitting an Oversize Skill

When a skill exceeds 300 lines: split it into a lean overview skill plus focused
skills (or move bulk to `code-samples/`/`references/`), then cross-link with
`Related Skills` sections. Keep the original name for the overview when possible.

## Removing a Skill

Search for references (`rg "<name>" .llm/`), delete the directory, regenerate the
index, and run the linters to catch dangling links.

## Related Skills

- [ship-changes](./ship-changes/SKILL.md) - validation loop to run before committing
- [bump-version-release](./bump-version-release/SKILL.md) - version sync rules
