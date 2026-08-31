# Historical build scripts

These retired bootstrap/hotfix workflows are retained for reference, outside
GitHub Actions discovery. Some embed old source snapshots and must not run
against the current application. Use ../workflows/build-release.yml instead.

Releases are explicitly dispatched, checked, staged as drafts, then published.
Publishing a tag must not trigger source-rewriting hotfixes or re-draft a release.
