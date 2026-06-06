# Code signing (free for open source)

Quiver's releases are currently **unsigned**, so Windows SmartScreen shows a one‑time
*"Windows protected your PC"* prompt. Signing the binaries removes that.

Because Quiver is **free and open source**, it qualifies for **free** code signing via
**[SignPath Foundation](https://signpath.org/)** — the certificate and signing service cost **$0**
for eligible OSS projects. Signing runs **in CI** (GitHub Actions), so releases are built and signed
automatically in the background; nothing runs on your machine.

The release workflow (`.github/workflows/release.yml`) is already wired for it — the signing steps
are **inert until you turn them on**.

## One‑time setup

1. **Apply** for the SignPath Foundation OSS plan at
   <https://signpath.org/apply> (or <https://about.signpath.io/product/open-source>). You'll link
   this GitHub repo. Approval is a manual review and can take a few days.

2. Once approved, in the **SignPath** dashboard:
   - Note your **Organization ID** (a GUID).
   - Create a **Project** with slug **`quiver`**.
   - Add an **Artifact Configuration** (slug **`initial`**) describing the files to sign
     (the two `.exe`s). The default "Authenticode" config works for a single exe; for both files,
     configure the artifact as a zip/folder or add a config per file and adjust the workflow's
     `artifact-configuration-slug` accordingly.
   - Create a **Signing Policy** with slug **`release-signing`** (release signing).
   - Create a **CI user / API token** for GitHub Actions and connect the GitHub repo as a
     trusted build system.

3. In this GitHub repo → **Settings → Secrets and variables → Actions**:
   - **Secret** `SIGNPATH_API_TOKEN` = the SignPath API token.
   - **Variable** `SIGNPATH_ORG_ID` = your SignPath Organization ID.
   - **Variable** `SIGNING_ENABLED` = `true`  ← this switch turns signing on.

   > The slugs in the workflow (`project-slug: quiver`, `signing-policy-slug: release-signing`,
   > `artifact-configuration-slug: initial`) must match what you created in SignPath. Edit
   > `release.yml` if you named them differently.

## Cutting a signed release

```bash
git tag v0.1.3
git push origin v0.1.3
```

The **Release** workflow builds the portable exe + installer, submits them to SignPath, waits for
the signed copies, and publishes them to a GitHub Release — all automatically. With `SIGNING_ENABLED`
unset, the same workflow still builds and releases, just **unsigned**.

You can also trigger the workflow manually (Actions → Release → *Run workflow*) to test the build/sign
steps without creating a release.

## Alternatives

- **Azure Trusted Signing** (~$9.99/month) — individual developers can sign up; clears SmartScreen
  via Microsoft's identity‑verified root. Good fallback if SignPath approval is slow. Swap the
  SignPath step for `azure/trusted-signing-action`.
- A traditional **OV** certificate (~$200–400/yr) builds SmartScreen reputation over time; **EV**
  (~$300–500/yr, hardware token) clears it immediately but is largely superseded by Trusted Signing.
