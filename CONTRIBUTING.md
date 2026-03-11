# Contributing to NoSleep

Thank you for considering contributing to NoSleep! Contributions are welcomed from everyone. By participating in this project, you agree to abide by the following guidelines and to license your contributions under The Unlicense (see [LICENSE](LICENSE)).

For build instructions see [BUILD.md](BUILD.md).

## How to Contribute

The project uses the standard GitHub fork + pull request workflow. If you're new to this process, here's a quick overview:

- Fork the repository on GitHub.
- Clone your fork locally.
- Create a new branch for your changes.
- Make your changes (following the guidelines below). Changes should target a single enhancement per branch.
- Test your changes thoroughly.
- Push your branch to your fork.
- Open a pull request against the main repository’s main (or master) branch.

## Development Guidelines

TLDR: keep it lean.

To keep the project consistent and maintainable, please adhere to the following guidelines:

### 1. Avoid External Dependencies

- Do not add external libraries or NuGet packages unless absolutely necessary. The project aims to be lightweight and self-contained. If you believe a dependency is essential, please explain why in your pull request.

### 2. Target a Single Executable

- The build process must produce a single standalone executable (for both .NET Framework 4.8 and .NET 8.0 targets).
- If your changes make it impossible to package into a single executable - please explain why that is necessary.

### 3. Keep It Lean

Avoid unnecessary overhead:

- Do not call external processes unless absolutely required (and if you do, justify it).
- Minimize allocations, CPU consumption, and exception throwing.
- Write efficient code – this is a background utility that should have minimal impact on system resources.

### 4. Maintain Cross‑Framework Compatibility

- NoSleep targets both .NET Framework 4.8 and .NET 8.0-windows.
- When adding new features or making changes, ensure they work on both frameworks.
- Use preprocessor directives (`#if NETFRAMEWORK` / `#if NET`) where necessary, but prefer code that works on both without conditional compilation.

## Testing Your Changes

- Test your changes on both target frameworks (Debug and Release configurations).
- Verify that the resulting executable runs as expected and that no new issues are introduced.
- If you're adding a feature, consider whether it needs a corresponding test (though the project currently has no formal test suite, manual verification is appreciated).

## Submitting a Pull Request

When you open a pull request, please:

- Provide a clear description of the changes and the motivation behind them.
- Reference any related issues (e.g., "Fixes #123").
- Ensure your branch is up to date with the latest main branch.

Keep the pull request focused on a single topic – if you have multiple unrelated changes, submit separate PRs.

## License Agreement

By contributing to NoSleep, you agree that your contributions will be licensed under The Unlicense. This means your work is dedicated to the public domain and can be used freely by anyone, for any purpose. If you are uncomfortable with this, please do not contribute.

## Need Help?

If you have questions or need guidance, feel free to open an issue or start a discussion.
