# Contributing

Thanks for your interest in contributing.

This repository is primarily a personal project focused on building a ZX Spectrum 128K emulator in a clean, testable, and incremental way.

Contributions are welcome, but the scope is intentionally limited to keep the architecture consistent.

---

## What Contributions Are Accepted

Please limit contributions to:

- Bug fixes
- Fixes must include tests

Feature contributions are generally **not accepted** unless they align directly with the current milestone and are discussed first.

---

## Project Principles (Non-Negotiable)

- Standard library only
- No third-party libraries or NuGet packages
- Incremental changes only
- Do not rewrite large sections of the codebase
- Maintain current architecture and structure

---

## Pull Request Requirements

All pull requests must:

- Pass `dotnet test`
- Include new tests for any behavioural changes
- Be small and focused
- Include a clear explanation of the change

If a change affects emulator behaviour, include:

- What changed
- Why it was incorrect before
- How it was validated (tests, manual steps, etc.)

---

## Development Workflow

1. Create a branch
2. Make a focused change
3. Add or update tests
4. Run:

   dotnet test

5. Open a pull request

---

## Discussion Before Large Changes

If you think a change might be large or affect architecture:

- Open an issue first
- Wait for feedback before implementing

---

## Final Decision

The maintainer has final say on all contributions.

This is to ensure the emulator stays aligned with its goals and remains a clean learning project.
