# GitHub Actions Workflow Enhancement Recommendations

This document provides detailed recommendations for enhancing the CI/CD pipeline for Cheap Shotcut Randomizer.

## Summary Table

| Enhancement | Priority | Complexity | Impact | Dependencies |
|-------------|----------|------------|--------|--------------|
| Docker Build Support | High | Medium | High | Docker Hub account |
| Multi-Platform Testing | Medium | High | High | None |
| Automated Changelog | High | Low | Medium | None |
| Performance Regression Testing | Low | High | Medium | Benchmark project |
| Integration Tests | High | High | High | Test infrastructure |
| Nightly Builds | Medium | Low | Low | None |
| Documentation Generation | Medium | Medium | Medium | DocFX/Doxygen |
| Blazor WASM Build | Low | Medium | Low | Major refactoring |
| GPU Testing | Low | Very High | High | Self-hosted runner with GPU |
| Release Automation | High | Medium | High | Semantic versioning |

---

## 1. Docker Build Support (Priority: HIGH)

### Description
Create Docker images for consistent deployment and easier distribution, especially for the Python/VapourSynth dependencies.

### Implementation

**New Workflow: `docker-build.yml`**
```yaml
name: Docker Build and Push

on:
  push:
    branches: [ master, dev ]
    tags: [ 'v*.*.*' ]
  pull_request:
    branches: [ master ]

jobs:
  docker:
    runs-on: ubuntu-latest
    steps:
    - name: Checkout
      uses: actions/checkout@v4

    - name: Set up Docker Buildx
      uses: docker/setup-buildx-action@v3

    - name: Login to Docker Hub
      if: github.event_name != 'pull_request'
      uses: docker/login-action@v3
      with:
        username: ${{ secrets.DOCKER_USERNAME }}
        password: ${{ secrets.DOCKER_PASSWORD }}

    - name: Extract metadata
      id: meta
      uses: docker/metadata-action@v5
      with:
        images: cheapnud/shotcut-randomizer
        tags: |
          type=ref,event=branch
          type=ref,event=pr
          type=semver,pattern={{version}}
          type=semver,pattern={{major}}.{{minor}}

    - name: Build and push
      uses: docker/build-push-action@v5
      with:
        context: .
        push: ${{ github.event_name != 'pull_request' }}
        tags: ${{ steps.meta.outputs.tags }}
        labels: ${{ steps.meta.outputs.labels }}
        cache-from: type=registry,ref=cheapnud/shotcut-randomizer:buildcache
        cache-to: type=registry,ref=cheapnud/shotcut-randomizer:buildcache,mode=max
```

**Dockerfile Required:**
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["CheapShotcutRandomizer.csproj", "./"]
RUN dotnet restore
COPY . .
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CheapShotcutRandomizer.dll"]
```

### Benefits
- Consistent runtime environment
- Easier deployment
- Better dependency isolation
- Simplified VapourSynth/Python setup

### Secrets Required
- `DOCKER_USERNAME`
- `DOCKER_PASSWORD`

---

## 2. Multi-Platform Testing (Priority: MEDIUM)

### Description
Test on Linux and macOS to ensure cross-platform compatibility (future-proofing).

### Implementation

**Update `ci.yml`:**
```yaml
strategy:
  matrix:
    os: [windows-latest, ubuntu-latest, macos-latest]
    configuration: [Debug, Release]
```

### Challenges
- **Windows-specific dependencies**: Avalonia UI, System.Management (WMI)
- **GPU dependencies**: CUDA/TensorRT only on NVIDIA GPUs
- **Platform-specific paths**: MLT files, executable detection

### Solutions
1. Use platform-conditional compilation: `#if WINDOWS`
2. Mock hardware-dependent services in tests
3. Create platform-specific build configurations
4. Skip GPU tests on non-Windows platforms

### Benefits
- Better code quality
- Future cross-platform support
- Catch platform-specific bugs early

---

## 3. Automated Changelog Generation (Priority: HIGH)

### Description
Automatically generate changelogs from commit messages and PR descriptions.

### Implementation

**New Workflow: `changelog.yml`**
```yaml
name: Generate Changelog

on:
  release:
    types: [published]
  workflow_dispatch:

jobs:
  changelog:
    runs-on: ubuntu-latest
    steps:
    - name: Checkout
      uses: actions/checkout@v4
      with:
        fetch-depth: 0

    - name: Generate Changelog
      uses: mikepenz/release-changelog-builder-action@v4
      with:
        configuration: ".github/changelog-config.json"
        outputFile: CHANGELOG.md
        commitMode: true
      env:
        GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}

    - name: Commit Changelog
      uses: stefanzweifel/git-auto-commit-action@v5
      with:
        commit_message: "docs: update CHANGELOG.md"
        file_pattern: CHANGELOG.md
```

**Configuration file: `.github/changelog-config.json`**
```json
{
  "categories": [
    {
      "title": "## 🚀 Features",
      "labels": ["feature", "enhancement"]
    },
    {
      "title": "## 🐛 Bug Fixes",
      "labels": ["bug", "fix"]
    },
    {
      "title": "## 📚 Documentation",
      "labels": ["documentation"]
    },
    {
      "title": "## 🔧 Maintenance",
      "labels": ["chore", "dependencies"]
    }
  ],
  "template": "#{{CHANGELOG}}\n\n**Full Changelog**: #{{RELEASE_DIFF}}",
  "pr_template": "- #{{TITLE}} (#{{NUMBER}})"
}
```

### Benefits
- Automatic release notes
- Standardized changelog format
- Better communication with users
- Reduced manual work

---

## 4. Integration Tests with AI Dependencies (Priority: HIGH)

### Description
Test the complete pipeline with real RIFE, Real-CUGAN, and VapourSynth integration.

### Implementation

**New Test Project:**
```
CheapShotcutRandomizer.IntegrationTests/
├── CheapShotcutRandomizer.IntegrationTests.csproj
├── VideoProcessingTests.cs
├── RifeIntegrationTests.cs
├── RealCuganIntegrationTests.cs
└── Fixtures/
    └── sample-video.mp4 (small test video)
```

**New Workflow: `integration-tests.yml`**
```yaml
name: Integration Tests

on:
  push:
    branches: [ master, dev ]
  pull_request:
    branches: [ master ]
  workflow_dispatch:

jobs:
  integration:
    runs-on: windows-latest
    steps:
    - name: Checkout
      uses: actions/checkout@v4

    - name: Setup .NET 10.0
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '10.0.x'
        dotnet-quality: 'preview'

    - name: Install FFmpeg
      run: choco install ffmpeg -y

    - name: Install Python
      uses: actions/setup-python@v5
      with:
        python-version: '3.11'

    - name: Install VapourSynth
      run: |
        choco install vapoursynth -y
        python -m pip install vapoursynth

    - name: Run Integration Tests
      run: dotnet test CheapShotcutRandomizer.IntegrationTests/ --verbosity normal
      env:
        INTEGRATION_TESTS_ENABLED: true
```

### Test Examples
```csharp
[Fact(Skip = "Integration test - requires VapourSynth")]
public async Task RifeInterpolation_ProcessesVideo_Successfully()
{
    // Arrange
    var inputVideo = "Fixtures/sample-video.mp4";
    var rifeService = new RifeInterpolationService(...);

    // Act
    var outputPath = await rifeService.InterpolateAsync(inputVideo, 60);

    // Assert
    Assert.True(File.Exists(outputPath));
    var videoInfo = await FFmpeg.GetMediaInfo(outputPath);
    Assert.Equal(60, videoInfo.VideoStreams.First().FrameRate);
}
```

### Benefits
- Catch integration issues early
- Validate AI upscaling pipeline
- Test real-world scenarios
- Ensure dependency compatibility

### Challenges
- Long-running tests (10+ minutes)
- Large test fixtures
- GPU dependency for full testing
- VapourSynth installation complexity

---

## 5. Nightly Builds (Priority: MEDIUM)

### Description
Scheduled builds from dev branch to catch issues early.

### Implementation

**New Workflow: `nightly.yml`**
```yaml
name: Nightly Build

on:
  schedule:
    - cron: '0 2 * * *'  # 2 AM UTC daily
  workflow_dispatch:

jobs:
  nightly-build:
    runs-on: windows-latest
    steps:
    - name: Checkout dev branch
      uses: actions/checkout@v4
      with:
        ref: dev

    - name: Setup .NET 10.0
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '10.0.x'
        dotnet-quality: 'preview'

    - name: Build and Test
      run: |
        dotnet restore
        dotnet build --configuration Release
        dotnet test --configuration Release

    - name: Publish Nightly Build
      run: |
        dotnet publish -c Release -r win-x64 --self-contained -o ./nightly
        $date = Get-Date -Format "yyyy-MM-dd"
        Compress-Archive -Path ./nightly/* -DestinationPath "nightly-$date.zip"

    - name: Upload to Releases
      uses: softprops/action-gh-release@v2
      with:
        name: "Nightly Build - $(date +%Y-%m-%d)"
        tag_name: "nightly-$(date +%Y%m%d)"
        prerelease: true
        files: nightly-*.zip
```

### Benefits
- Early detection of breaking changes
- Regular testing of dev branch
- Nightly builds for testers
- Continuous validation

---

## 6. Documentation Generation (Priority: MEDIUM)

### Description
Auto-generate API documentation from XML comments.

### Implementation

**New Workflow: `docs.yml`**
```yaml
name: Generate Documentation

on:
  push:
    branches: [ master ]
  workflow_dispatch:

jobs:
  docs:
    runs-on: windows-latest
    steps:
    - name: Checkout
      uses: actions/checkout@v4

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '10.0.x'
        dotnet-quality: 'preview'

    - name: Install DocFX
      run: dotnet tool install -g docfx

    - name: Generate Documentation
      run: docfx docs/docfx.json

    - name: Publish to GitHub Pages
      uses: peaceiris/actions-gh-pages@v4
      with:
        github_token: ${{ secrets.GITHUB_TOKEN }}
        publish_dir: ./docs/_site
```

**Required: `docs/docfx.json`**
```json
{
  "metadata": [{
    "src": [{
      "src": "..",
      "files": ["**/*.csproj"],
      "exclude": ["**/bin/**", "**/obj/**"]
    }],
    "dest": "api"
  }],
  "build": {
    "content": [{
      "files": ["api/**.yml", "*.md"]
    }],
    "dest": "_site"
  }
}
```

### Benefits
- Always up-to-date API documentation
- Better code discoverability
- Professional appearance
- Hosted on GitHub Pages

---

## 7. Semantic Release Automation (Priority: HIGH)

### Description
Automate version bumping, changelog generation, and releases based on conventional commits.

### Implementation

**New Workflow: `semantic-release.yml`**
```yaml
name: Semantic Release

on:
  push:
    branches: [ master ]

jobs:
  release:
    runs-on: ubuntu-latest
    steps:
    - name: Checkout
      uses: actions/checkout@v4
      with:
        fetch-depth: 0
        persist-credentials: false

    - name: Semantic Release
      uses: cycjimmy/semantic-release-action@v4
      with:
        extra_plugins: |
          @semantic-release/changelog
          @semantic-release/git
      env:
        GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

**Configuration: `.releaserc.json`**
```json
{
  "branches": ["master"],
  "plugins": [
    "@semantic-release/commit-analyzer",
    "@semantic-release/release-notes-generator",
    "@semantic-release/changelog",
    "@semantic-release/github",
    "@semantic-release/git"
  ]
}
```

### Commit Message Format
```
<type>(<scope>): <subject>

Types: feat, fix, docs, style, refactor, perf, test, chore
Examples:
  feat(rife): add RIFE 4.26 model support
  fix(upscaling): fix Real-CUGAN memory leak
  docs: update installation guide
```

### Benefits
- Automatic versioning (semantic)
- Changelog generation
- Release creation
- Standardized commit messages

---

## 8. GPU Testing with Self-Hosted Runner (Priority: LOW)

### Description
Test AI upscaling on real GPU hardware using self-hosted runner.

### Implementation

**Setup:**
1. Configure Windows machine with NVIDIA GPU
2. Install GitHub Actions runner
3. Tag runner: `gpu`, `cuda`, `tensorrt`

**New Workflow: `gpu-tests.yml`**
```yaml
name: GPU Integration Tests

on:
  push:
    branches: [ master, dev ]
    paths:
      - 'Services/RIFE/**'
      - 'Services/RealCUGAN/**'
      - 'Services/RealESRGAN/**'
  workflow_dispatch:

jobs:
  gpu-tests:
    runs-on: [self-hosted, windows, gpu]
    timeout-minutes: 120

    steps:
    - name: Checkout
      uses: actions/checkout@v4

    - name: Verify GPU
      run: nvidia-smi

    - name: Setup .NET 10.0
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '10.0.x'
        dotnet-quality: 'preview'

    - name: Run GPU Tests
      run: dotnet test --filter "Category=GPU" --verbosity normal
      env:
        CUDA_VISIBLE_DEVICES: 0
```

### Benefits
- Real GPU testing
- Validate CUDA/TensorRT integration
- Performance benchmarking on real hardware
- Catch GPU-specific bugs

### Costs
- Requires dedicated hardware
- Maintenance overhead
- Higher complexity

---

## 9. Artifact Retention and Cleanup (Priority: MEDIUM)

### Description
Automatically clean up old artifacts to save storage costs.

### Implementation

**New Workflow: `cleanup.yml`**
```yaml
name: Cleanup Old Artifacts

on:
  schedule:
    - cron: '0 0 * * 0'  # Weekly on Sunday
  workflow_dispatch:

jobs:
  cleanup:
    runs-on: ubuntu-latest
    steps:
    - name: Delete old artifacts
      uses: c-hive/gha-remove-artifacts@v1
      with:
        age: '30 days'
        skip-recent: 5
```

### Benefits
- Reduced storage costs
- Cleaner artifact list
- Automatic maintenance

---

## 10. Performance Regression Detection (Priority: LOW)

### Description
Compare benchmark results against baseline to detect performance regressions.

### Implementation

**Update `benchmark.yml`:**
```yaml
- name: Compare against baseline
  uses: benchmark-action/github-action-benchmark@v1
  with:
    tool: 'benchmarkdotnet'
    output-file-path: BenchmarkDotNet.Artifacts/results/results.json
    github-token: ${{ secrets.GITHUB_TOKEN }}
    alert-threshold: '150%'
    comment-on-alert: true
    fail-on-alert: true
    auto-push: ${{ github.event_name == 'push' }}
```

### Benefits
- Catch performance regressions
- Historical performance tracking
- Data-driven optimization

---

## 11. Blazor Component Testing (Priority: MEDIUM)

### Description
Add comprehensive BUnit tests for Blazor components.

### Current Status
- Test project exists: `CheapShotcutRandomizer.Tests`
- BUnit package installed (v1.33.3)

### Enhancement Needed
Expand component test coverage for:
- `AddRenderJobDialog.razor`
- `FirstRunWizard.razor`
- `Settings.razor`
- All page components

**Example Test:**
```csharp
using Bunit;

public class AddRenderJobDialogTests : TestContext
{
    [Fact]
    public void Dialog_RendersCorrectly()
    {
        // Arrange
        var cut = RenderComponent<AddRenderJobDialog>();

        // Assert
        cut.Find("h6").TextContent.Should().Contain("Add Render Job");
    }

    [Fact]
    public async Task SubmitButton_ValidatesInput()
    {
        // Arrange
        var cut = RenderComponent<AddRenderJobDialog>();

        // Act
        await cut.Find("button[type=submit]").ClickAsync(new());

        // Assert
        cut.FindAll(".mud-input-error").Should().NotBeEmpty();
    }
}
```

### Benefits
- Catch UI regressions
- Test component interactions
- Validate Blazor bindings
- Improve code confidence

---

## 12. Dependabot Configuration (Priority: HIGH)

### Description
Configure Dependabot for automatic dependency updates.

### Implementation

**File: `.github/dependabot.yml`**
```yaml
version: 2
updates:
  # NuGet dependencies
  - package-ecosystem: "nuget"
    directory: "/"
    schedule:
      interval: "weekly"
      day: "monday"
    open-pull-requests-limit: 10
    reviewers:
      - "CheapNud"
    labels:
      - "dependencies"
      - "nuget"
    groups:
      microsoft:
        patterns:
          - "Microsoft.*"
      testing:
        patterns:
          - "xunit*"
          - "bunit"
          - "Moq"
          - "FluentAssertions"

  # GitHub Actions
  - package-ecosystem: "github-actions"
    directory: "/"
    schedule:
      interval: "weekly"
    labels:
      - "dependencies"
      - "github-actions"
```

### Benefits
- Automatic security updates
- Stay current with dependencies
- Grouped updates for easier review
- Reduce manual maintenance

---

## 13. Branch Protection and Required Checks (Priority: HIGH)

### Description
Configure branch protection rules to enforce quality standards.

### Implementation

**GitHub Settings → Branches → Branch protection rules:**

**For `master` branch:**
- ✅ Require a pull request before merging
- ✅ Require approvals: 1
- ✅ Dismiss stale pull request approvals when new commits are pushed
- ✅ Require status checks to pass before merging:
  - `build-and-test (windows-latest, Debug)`
  - `build-and-test (windows-latest, Release)`
  - `code-quality`
  - `dependency-check`
  - `dependency-review`
  - `CodeQL Analysis`
- ✅ Require conversation resolution before merging
- ✅ Require signed commits (optional)
- ✅ Include administrators

**For `dev` branch:**
- ✅ Require a pull request before merging
- ✅ Require status checks to pass before merging:
  - `build-and-test (windows-latest, Debug)`
  - `build-and-test (windows-latest, Release)`
- ✅ Include administrators

### Benefits
- Enforce code review
- Prevent broken builds from merging
- Maintain code quality
- Standardize development process

---

## 14. Issue Templates (Priority: MEDIUM)

### Description
Create standardized issue templates for bug reports and feature requests.

### Implementation

**File: `.github/ISSUE_TEMPLATE/bug_report.yml`**
```yaml
name: Bug Report
description: Report a bug or unexpected behavior
labels: ["bug", "triage"]
body:
  - type: markdown
    attributes:
      value: |
        Thanks for taking the time to report a bug!

  - type: textarea
    id: description
    attributes:
      label: Description
      description: A clear description of the bug
    validations:
      required: true

  - type: textarea
    id: reproduction
    attributes:
      label: Steps to Reproduce
      description: Detailed steps to reproduce the issue
      placeholder: |
        1. Open application
        2. Click on...
        3. See error
    validations:
      required: true

  - type: textarea
    id: expected
    attributes:
      label: Expected Behavior
      description: What should happen?
    validations:
      required: true

  - type: textarea
    id: actual
    attributes:
      label: Actual Behavior
      description: What actually happens?
    validations:
      required: true

  - type: input
    id: version
    attributes:
      label: Version
      description: Application version (e.g., 2.0.0)
    validations:
      required: true

  - type: dropdown
    id: os
    attributes:
      label: Operating System
      options:
        - Windows 10
        - Windows 11
    validations:
      required: true

  - type: input
    id: gpu
    attributes:
      label: GPU
      description: GPU model (e.g., RTX 3080)

  - type: textarea
    id: logs
    attributes:
      label: Logs
      description: Relevant log output
      render: shell
```

**File: `.github/ISSUE_TEMPLATE/feature_request.yml`**
```yaml
name: Feature Request
description: Suggest a new feature
labels: ["enhancement", "triage"]
body:
  - type: textarea
    id: problem
    attributes:
      label: Problem Description
      description: What problem does this feature solve?
    validations:
      required: true

  - type: textarea
    id: solution
    attributes:
      label: Proposed Solution
      description: How should this feature work?
    validations:
      required: true

  - type: textarea
    id: alternatives
    attributes:
      label: Alternatives Considered
      description: Other solutions you've considered

  - type: dropdown
    id: priority
    attributes:
      label: Priority
      options:
        - Low
        - Medium
        - High
```

### Benefits
- Standardized bug reports
- Better issue triage
- Collect necessary information upfront
- Reduce back-and-forth

---

## Implementation Priority Roadmap

### Phase 1: Foundation (Week 1)
1. ✅ Create basic CI/CD workflows
2. Dependabot configuration
3. Branch protection rules
4. Issue templates

### Phase 2: Quality (Week 2-3)
1. Expand unit test coverage
2. Add BUnit component tests
3. Automated changelog
4. Documentation generation

### Phase 3: Advanced (Week 4+)
1. Integration tests
2. Docker support
3. Semantic release
4. Performance benchmarking

### Phase 4: Future
1. Multi-platform support
2. GPU testing (self-hosted runner)
3. Nightly builds
4. Advanced monitoring

---

## Cost Considerations

### GitHub Actions Minutes
- Free tier: 2,000 minutes/month (private repos)
- Public repos: Unlimited
- Windows runners: 2x multiplier
- Self-hosted runners: Free

### Estimated Usage (Monthly)
- CI builds: ~500 minutes
- Tests: ~300 minutes
- CodeQL: ~200 minutes
- Releases: ~50 minutes
- **Total: ~1,050 minutes (~2,100 with Windows multiplier)**

### Recommendations
1. Use self-hosted runners for heavy workloads
2. Cache dependencies aggressively
3. Skip redundant tests on documentation changes
4. Use manual triggers for expensive workflows

---

## Metrics and Monitoring

### Key Metrics to Track
1. **Build Success Rate**: Target >95%
2. **Test Coverage**: Target >80%
3. **Average Build Time**: Target <10 minutes
4. **Time to Merge PR**: Target <2 days
5. **Deployment Frequency**: Target weekly

### Dashboard Tools
- GitHub Insights
- CodeQL security alerts
- Dependabot alerts
- Action run history

---

## Conclusion

These enhancements will significantly improve the development workflow, code quality, and release process for Cheap Shotcut Randomizer. Implement them incrementally based on priority and available resources.

For questions or suggestions, please open an issue or discussion on GitHub.
