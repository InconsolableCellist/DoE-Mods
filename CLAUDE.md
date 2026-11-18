# Rules for AI agents working in this repo

- Never add "Co-Authored-By: Claude", "Generated with Claude Code", a `Claude-Session:` line, or
  any similar AI attribution to commit messages, PR descriptions, PR/issue comments, or code.
  Commit as the repo owner's git identity, never as "Claude <noreply@anthropic.com>".
- Before merging any branch, check its commits' authors and trailers
  (`git log --format='%an <%ae>%n%B' master..<branch>`), not just the diff. Rewrite any commit
  that breaks the rule above before it reaches master.
