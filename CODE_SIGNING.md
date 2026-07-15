# Code Signing Policy

> **Current status (July 2026):** Windows release binaries are **not signed yet**. We applied to
> the [SignPath Foundation](https://signpath.org)'s free open-source code-signing program in July
> 2026; the application was declined for now because the project does not yet meet the program's
> public-visibility criteria (community adoption, external references). We plan to reapply once
> the project has grown. Until then, Microsoft Defender SmartScreen may warn about an "unknown
> publisher" — see the README's Windows security notice.

This document describes how releases of **Blocks Beyond the Stars** are built and authorized, and
how code signing will work once a certificate is available.

## Official repository

The only official source of Blocks Beyond the Stars is:

<https://github.com/marceld23/BlocksBeyondTheStars>

Official builds are published exclusively on this project's
[GitHub Releases](https://github.com/marceld23/BlocksBeyondTheStars/releases) and mirrored to the
official [itch.io page](https://jumavegames.itch.io/blocks-beyond-the-stars). Binaries obtained from
anywhere else are not covered by this policy and should not be trusted.

## What will be signed, and how

Once a code-signing certificate is available:

- Only the **Windows installer artifacts** will be signed: the per-user installer (`*Setup.exe`),
  the machine-wide MSI (`*.msi`), and the portable ZIP (`*Portable.zip`).
- Signing will happen **only** inside the automated GitHub Actions release workflow
  ([`.github/workflows/release.yml`](.github/workflows/release.yml)), which builds exclusively from
  source in the public repository above. No locally or manually produced binary will ever be signed.
- A signed release will be produced only from a version tag (`vX.Y.Z`) pushed by a project
  maintainer; the same tag is the single source of truth for the version baked into the build.
- The Linux and (experimental) macOS builds are **not** covered by this policy. The macOS
  build is unsigned and un-notarized by design — see the README security notices.

The tag-driven, CI-only release process described above is already in place today — only the
signing step itself is missing.

## Who may authorize a release

Releases are authorized and tagged by the project maintainer(s):

- **Marcel Dütscher** ([@marceld23](https://github.com/marceld23))

Maintainer GitHub accounts have two-factor authentication (2FA) enabled.

## Privacy

Code signing operates on the build artifacts described above; it does not process end-user data.
See this project's [privacy policy](https://www.blocksbeyondthestars.com/en/datenschutz).
