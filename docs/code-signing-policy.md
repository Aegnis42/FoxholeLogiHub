# Code Signing Policy / Politique de signature de code

*This page documents how FoxholeLogiHub releases are built, reviewed and signed.*

## Team and roles

| Role | Member | Responsibility |
|---|---|---|
| Committer | [@Aegnis42](https://github.com/Aegnis42) | Trusted to modify the source code |
| Reviewer | [@Aegnis42](https://github.com/Aegnis42) | Reviews all external contributions before merge |
| Approver | [@Aegnis42](https://github.com/Aegnis42) | Authorizes releases for signing |

External contributions (pull requests) are always reviewed by the maintainer before being
merged. Repository access is protected by multi-factor authentication.

## Build process

Every release is built **from the public source code** of this repository by GitHub Actions
([release workflow](../.github/workflows/release.yml)) :

1. A version tag (`vX.Y.Z`) is pushed by the approver.
2. GitHub Actions restores, builds and tests the solution, then packages the application with
   Velopack (installer + delta updates).
3. The resulting artifacts are published on the
   [GitHub Releases](https://github.com/Aegnis42/FoxholeLogiHub/releases) page.

No binary is ever published from a developer machine. The product name (`FoxholeLogiHub`) and
the product version (matching the release tag) are embedded in every signed binary.

## Signing

<!-- À activer après acceptation par SignPath Foundation :
Free code signing provided by [SignPath.io](https://signpath.io), certificate by
[SignPath Foundation](https://signpath.org).
Each release is manually approved for signing by the approver.
-->

*Application to SignPath Foundation in progress — releases are currently unsigned.*

## Privacy

This program transfers data to the project server only for the team features explicitly used
by the user (Steam sign-in, regiment, shared stockpiles, supply requests). Details, including
third-party components: see [PRIVACY.md](../PRIVACY.md).
