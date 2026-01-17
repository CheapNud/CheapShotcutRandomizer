# GitHub Actions Workflows Documentation

This document describes all GitHub Actions workflows configured for the Cheap Shotcut Randomizer project.

## Workflow Files

### 1. CI Build and Test (`ci.yml`)

**Triggers:**
- Push to `master`, `dev`, or feature branches (`f/**`)
- Pull requests to `master` or `dev`
- Manual workflow dispatch

**Jobs:**
- **build-and-test**: Builds and tests the solution on Windows with both Debug and Release configurations
  - Checks out code and CheapHelpers dependency
  - Sets up .NET 10.0 preview
  - Restores NuGet packages (with caching)
  - Builds solution
  - Runs tests with coverage collection
  - Uploads test results and coverage reports
  - Publishes build artifacts (Release only)

- **code-quality**: Performs code quality checks
  - Verifies code formatting with `dotnet format`
  - Runs static code analysis

- **dependency-check**: Scans for dependency vulnerabilities
  - Checks for vulnerable packages
  - Checks for deprecated packages
  - Checks for outdated packages

**Artifacts:**
- Test results (.trx files)
- Code coverage reports
- Build artifacts (Release configuration)

**Retention:** 7-30 days

---

### 2. Release Build and Publish (`release.yml`)

**Triggers:**
- Tag push matching `v*.*.*` (e.g., v2.0.0)
- Manual workflow dispatch with version input

**Jobs:**
- **build-release**: Creates release packages
  - Builds self-contained Windows x64 package (~100MB)
  - Builds framework-dependent Windows x64 package (~10MB)
  - Runs tests before packaging
  - Creates ZIP archives
  - Generates SHA256 checksums
  - Creates GitHub Release with detailed release notes

- **publish-nuget**: (Currently disabled) Publishes NuGet packages
  - Set `if: false` to `if: true` to enable
  - Requires `NUGET_API_KEY` secret

**Release Packages:**
- `CheapShotcutRandomizer-v{VERSION}-win-x64-standalone.zip` - Self-contained
- `CheapShotcutRandomizer-v{VERSION}-win-x64.zip` - Framework-dependent
- `checksums.txt` - SHA256 verification

**Secrets Required:**
- `GITHUB_TOKEN` (automatic)
- `NUGET_API_KEY` (optional, for NuGet publishing)

---

### 3. CodeQL Security Scanning (`codeql.yml`)

**Triggers:**
- Push to `master` or `dev`
- Pull requests to `master` or `dev`
- Weekly schedule (Mondays at 8:00 UTC)
- Manual workflow dispatch

**Security Analysis:**
- Scans C# code for security vulnerabilities
- Uses security-and-quality query suite
- Reports findings to GitHub Security tab

**Permissions:**
- `actions: read`
- `contents: read`
- `security-events: write`

---

### 4. Dependency Review (`dependency-review.yml`)

**Triggers:**
- Pull requests to `master` or `dev`

**Checks:**
- Reviews dependency changes in PRs
- Fails on moderate or higher severity vulnerabilities
- Allowed licenses: MIT, Apache-2.0, BSD-3-Clause, BSD-2-Clause, ISC, 0BSD, Unlicense
- Posts summary comment on PR

---

### 5. Stale Issue/PR Management (`stale.yml`)

**Triggers:**
- Daily schedule (midnight UTC)
- Manual workflow dispatch

**Behavior:**
- Marks issues stale after 60 days of inactivity
- Marks PRs stale after 30 days of inactivity
- Closes stale issues after 14 days
- Closes stale PRs after 7 days
- Exempts issues with labels: `pinned`, `security`, `bug`, `enhancement`
- Exempts PRs with labels: `work-in-progress`, `blocked`

---

### 6. Pull Request Labeler (`labeler.yml`)

**Triggers:**
- Pull request opened, synchronized, or reopened

**Auto-labels based on changed files:**
- **Areas**: `area/ui`, `area/services`, `area/models`, `area/tests`, `area/ai-upscaling`, `area/frame-interpolation`, `area/video-processing`, `area/dependencies`
- **Types**: `type/documentation`, `type/config`, `type/build`
- **Sizes**: `size/XS` (≤10), `size/S` (11-50), `size/M` (51-200), `size/L` (201-500), `size/XL` (>500)
- **Dependencies**: Triggered by csproj or lock file changes

**Configuration:** `.github/labeler.yml`

---

### 7. Code Coverage Report (`coverage.yml`)

**Triggers:**
- Push to `master` or `dev`
- Pull requests to `master` or `dev`
- Manual workflow dispatch

**Features:**
- Generates detailed HTML coverage reports
- Creates coverage summary in Markdown
- Posts coverage summary as PR comment
- Uploads coverage artifacts
- Optional Codecov integration (currently disabled)

**Tools:**
- coverlet for collection
- ReportGenerator for report generation
- Formats: HTML, Cobertura, Markdown

**Artifacts:**
- HTML coverage report (30-day retention)

---

### 8. Performance Benchmarks (`benchmark.yml`)

**Triggers:**
- Manual workflow dispatch
- PRs with `performance` label

**Notes:**
- Only runs if benchmark project exists
- Uses BenchmarkDotNet framework
- Uploads results as artifacts
- Posts summary comment on PRs

**Conditional Execution:** Only runs when explicitly needed to save CI minutes

---

## Configuration Files

### `.github/labeler.yml`
Defines auto-labeling rules for pull requests based on file paths and change sizes.

### `.github/PULL_REQUEST_TEMPLATE.md`
Standard PR template requiring:
- Description and type of change
- Related issues
- Testing checklist
- Performance and breaking change notes

---

## Setup Requirements

### Secrets
Configure these in repository Settings → Secrets and variables → Actions:

- `GITHUB_TOKEN` - Automatically provided by GitHub
- `NUGET_API_KEY` - (Optional) For NuGet package publishing

### Repository Settings

1. **Branch Protection Rules** (recommended):
   - Require PR reviews before merging
   - Require status checks to pass:
     - `build-and-test`
     - `code-quality`
     - `dependency-review`
   - Require branches to be up to date
   - Include administrators

2. **Security**:
   - Enable Dependabot alerts
   - Enable Dependabot security updates
   - Enable CodeQL scanning

3. **Actions Permissions**:
   - Allow all actions and reusable workflows
   - Allow GitHub Actions to create and approve pull requests (for automation)

### CheapHelpers Dependency

All workflows check out the CheapHelpers repository from `CheapNud/CheapHelpers`. Ensure:
- The repository exists and is accessible
- `GITHUB_TOKEN` has read access to the repository
- If private, configure a Personal Access Token with `repo` scope

---

## .NET 10.0 Support

All workflows use:
```yaml
dotnet-version: '10.0.x'
dotnet-quality: 'preview'
```

This configures .NET 10.0 RC/preview versions. When .NET 10.0 is officially released:
1. Remove `dotnet-quality: 'preview'`
2. Update to `dotnet-version: '10.0.x'` for latest patch

---

## Workflow Status Badges

Add these badges to your README.md:

```markdown
[![CI](https://github.com/CheapNud/ShotcutRandomizer/actions/workflows/ci.yml/badge.svg)](https://github.com/CheapNud/ShotcutRandomizer/actions/workflows/ci.yml)
[![CodeQL](https://github.com/CheapNud/ShotcutRandomizer/actions/workflows/codeql.yml/badge.svg)](https://github.com/CheapNud/ShotcutRandomizer/actions/workflows/codeql.yml)
[![Coverage](https://github.com/CheapNud/ShotcutRandomizer/actions/workflows/coverage.yml/badge.svg)](https://github.com/CheapNud/ShotcutRandomizer/actions/workflows/coverage.yml)
```

---

## Troubleshooting

### CheapHelpers Checkout Fails
**Error**: "Repository not found" or "Resource not accessible"

**Solutions:**
1. Verify the repository path is correct
2. If private, create a Personal Access Token with `repo` scope
3. Add token as `CHEAPHELPERS_PAT` secret
4. Update workflows to use:
   ```yaml
   token: ${{ secrets.CHEAPHELPERS_PAT }}
   ```

### .NET 10.0 Not Found
**Error**: "Version 10.0.x not found"

**Solutions:**
1. Check if .NET 10.0 is released
2. Verify `dotnet-quality: 'preview'` is set for RC/preview versions
3. Pin to specific version: `dotnet-version: '10.0.100-rc.2.24502.107'`

### Tests Fail on CI but Pass Locally
**Common causes:**
1. Missing dependencies (FFmpeg, VapourSynth, etc.)
2. Platform-specific behavior (Windows vs Linux)
3. File path differences
4. Time zone or culture-specific issues

**Solutions:**
1. Mock external dependencies in tests
2. Use platform-conditional tests
3. Use `Path.Combine()` for paths
4. Set explicit culture in tests

### Coverage Report Missing
**Error**: Coverage report not generated

**Solutions:**
1. Verify tests are running successfully
2. Check coverlet is collecting coverage: `--collect:"XPlat Code Coverage"`
3. Ensure ReportGenerator installation succeeds
4. Check artifact upload paths match report output

---

## Best Practices

1. **Commit Quality**:
   - Ensure code is formatted before committing
   - Run tests locally before pushing
   - Keep commits focused and atomic

2. **Pull Requests**:
   - Fill out the PR template completely
   - Wait for all checks to pass
   - Address review comments promptly
   - Keep PRs reasonably sized (aim for size/S or size/M)

3. **Release Process**:
   - Create release from `master` branch
   - Tag with semantic version: `vMAJOR.MINOR.PATCH`
   - Update version in csproj before tagging
   - Test release packages before publishing

4. **Security**:
   - Review Dependabot PRs weekly
   - Address CodeQL findings promptly
   - Keep dependencies up to date
   - Never commit secrets or credentials

---

## Future Enhancements

See `WORKFLOW_ENHANCEMENTS.md` for detailed enhancement suggestions.
