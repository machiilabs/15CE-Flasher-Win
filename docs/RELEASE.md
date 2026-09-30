# Release checklist (Mac developer)

You never need a local Windows PC to **build**. You only need Windows hardware to **test** USB.

## Bump the version

Change these together, and use a new build number for every build you hand to anyone:

- `.github/workflows/ci.yml`: `APP_VERSION`, `APP_BUILD`, `CDN_FILENAME` (for example `15CEFlasher-Win-1.2.0-113.exe`)
- `src/FifteenCEFlasher/FifteenCEFlasher.csproj`: `Version`, `FileVersion`, `InformationalVersion`, `AssemblyVersion`

Never upload a different file under a name that is already on downloads.machiilabs.com; the CDN and browsers cache it for hours. Bump the build instead.

## Build the `.exe` (from your Mac)

1. Push to GitHub, open a pull request, **or** open **Actions → CI → Run workflow** on your branch.
2. Wait for the green **windows-latest** job (about 2 minutes).
3. Open the run → **Artifacts** → download the `.exe` (same name as `CDN_FILENAME`).

The job summary lists the **SHA-256**.

## Test

Hand the `.exe` to testers with a direct downloads.machiilabs.com link. The public site keeps offering the last release until you ship.

## Ship a release

`main` is the release branch. It is protected: changes arrive only through pull requests the owner merges (see `.cursor/rules/protected-release-branch.mdc`).

1. Open a pull request from the release work into `main`. The owner merges it.
2. Tag the merge commit `vX.Y.Z` (annotated: “15CE Flasher for Windows X.Y.Z (build N)”) and push the tag.
3. Upload the tested `.exe` to **downloads.machiilabs.com** (same bucket as the Mac DMGs).
4. In the **machiilabs.com** repo, on a `flasher-win-X.Y.Z` branch from `origin/main`:
   - `src/lib/downloads.ts` → `PRODUCT_DOWNLOADS.winflasher` URL
   - `src/app/flasher/page.tsx` → `WIN_VERSION`, `WIN_BUILD`, `WIN_FILENAME`, `WIN_SHA256`
   - `src/app/flasher/guide/page.tsx` → `WIN_FILENAME`, `WIN_SHA256`
5. Open a pull request into `main` with the label `ship:flasher-win-X.Y.Z` once the owner says to ship. The release check fails if the page, guide, and download link disagree. Merging publishes the site.

## Local dev on Mac

Core library + unit tests only:

```bash
export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec"
export PATH="/opt/homebrew/opt/dotnet@8/bin:$PATH"
dotnet test tests/FifteenCEFlasherCore.Tests/FifteenCEFlasherCore.Tests.csproj -c Release
```

The WPF app builds only on Windows (CI or a VM).
