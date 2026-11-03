# Code signing policy

Free code signing for Snarvo is provided by [SignPath.io](https://about.signpath.io), with a
certificate by [SignPath Foundation](https://signpath.org).

## What is signed

Only the release builds of Snarvo published on this repository's
[Releases](https://github.com/andreasgmg/snarvo/releases) page: `Snarvo.exe` and `Snarvo.dll`.
Third-party components bundled with the app (the Windows App SDK, Win2D and the .NET runtime) are
already signed by Microsoft and are not re-signed.

Every signed release is built by GitHub Actions from a tagged commit in this repository
(see [`.github/workflows/build.yml`](.github/workflows/build.yml)). Nothing built on a personal
computer is ever submitted for signing.

## Team roles

| Role | Members |
|---|---|
| Committers and reviewers | [Andreas Glans](https://github.com/andreasgmg) |
| Approvers | [Andreas Glans](https://github.com/andreasgmg) |

Contributions from others are reviewed by a committer before they are merged. Every signing request
is approved manually by an approver.

## Privacy policy

This program will not transfer any information to other networked systems unless specifically
requested by the user or the person installing or operating it.

Snarvo contains no networking code at all. Screenshots, settings and recognised text stay on the
user's computer. See [SAKERHET.md](SAKERHET.md) (Swedish) for a full description of what the app
stores and where.
