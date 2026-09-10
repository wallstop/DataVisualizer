# Development and validation

The repository keeps Unity experiments, benchmark fixtures, captures, and generated
site output outside the package payload. From the repository root:

```bash
python3 scripts/docs/validate_docs.py
python3 -m pip install -r requirements-docs.txt
mkdocs build --strict --clean --site-dir /tmp/data-visualizer-site
pwsh -NoProfile -File scripts/tests/run-all.ps1
npm pack --dry-run
```

The documentation validator checks local Markdown links, image inventory, JSON
examples, schema versions, and the absence of generated site output in the package.
MkDocs strict mode treats warnings as failures. The Pages workflow is
documentation-only: it builds and deploys prepared Markdown, never installs or
starts Unity, and does not need Unity credentials.

## Contributions

Format changed C# with CSharpier 1.1.2, preserve `.meta` files beside Unity assets,
add focused EditMode/PlayMode coverage where behavior changes, and keep generated
benchmark/capture/site artifacts out of commits. Record measured limitations and
rollback commits in the PR description.
