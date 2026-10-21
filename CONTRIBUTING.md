# Contributing

- **Use [conventional commits].** Example: `feat: add login feature`.
- **Add unit tests.** Ensure all new features and bug fixes are covered by unit tests. Run existing tests before submitting your PR.
- **Write documentation.** Update the `Documentation~/` directory with relevant information about your changes.
- **Small Pull Requests.** Submit a few small PRs instead of a single large one. This makes reviewing faster and more manageable.
- **Minimalism and simplicity.** Prioritize minimalistic, simple, and user-friendly solutions. Avoid unnecessary complexity in project namespace.
- **Avoid overly complex names.** Simple names are usually more readable and easier to understand, especially in mathematical expressions and for internal use.
- **Configure secrets.** Optionally, configure the GitHub Actions secrets `UNITY_LICENSE`, `UNITY_EMAIL`, and `UNITY_PASSWORD` to enable CI pipelines for pull requests from your fork. See [game-ci] for more details.
- **Skip unreleased changelog.** Do not add entries to the Unreleased section. Changelog updates are reserved for release commits.

[conventional commits]: https://www.conventionalcommits.org/en/v1.0.0/
[game-ci]: https://game.ci/docs/github/activation#setting-up-your-unity-license
