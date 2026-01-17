# CheapShotcutRandomizer Test Suite

Comprehensive unit tests for the ShotcutRandomizer Blazor application using BUnit, xUnit, Moq, and FluentAssertions.

## Test Project Setup

### Prerequisites

- .NET 10.0 SDK
- Visual Studio 2022 (or Visual Studio Code with C# extension)

### NuGet Packages

The test project uses the following packages:

- **BUnit 1.33.3** - Blazor component testing framework
- **xUnit 2.9.3** - Unit testing framework
- **Moq 4.20.72** - Mocking framework for dependencies
- **FluentAssertions 7.0.0** - Fluent assertion library
- **Microsoft.NET.Test.Sdk 17.14.1** - Test SDK for running tests
- **coverlet.collector 6.0.4** - Code coverage collector

### Installation

To restore all dependencies:

```bash
cd CheapShotcutRandomizer.Tests
dotnet restore
```

## Running Tests

### Visual Studio

1. Open the solution in Visual Studio
2. Build the solution (Ctrl+Shift+B)
3. Open Test Explorer (Test > Test Explorer)
4. Click "Run All Tests"

### Command Line

Run all tests:

```bash
dotnet test
```

Run tests with detailed output:

```bash
dotnet test --verbosity detailed
```

Run tests with code coverage:

```bash
dotnet test --collect:"XPlat Code Coverage"
```

Run specific test class:

```bash
dotnet test --filter "FullyQualifiedName~RenderQueueTests"
```

Run specific test method:

```bash
dotnet test --filter "FullyQualifiedName~RenderQueueTests.RenderQueue_Renders_Successfully"
```

## Test Coverage

### Blazor Components (BUnit Tests)

#### RenderQueueTests
- Component rendering and initialization
- Hardware information display
- Queue status (paused/running) display
- Active/completed/failed jobs display
- Empty state handling
- Queue control buttons (start/pause)
- Job management actions (pause, resume, cancel, retry, delete)
- Queue statistics display
- Event subscription and cleanup

**Test Count:** 17 tests

#### RenderJobCardTests
- Job information display
- Status indicators (Pending, Running, Completed, Failed, Paused)
- Progress tracking and display
- File size formatting
- Two-stage render support
- Current stage display
- Action buttons and event callbacks
- Timecode conversion
- Error details display
- Component disposal and cleanup

**Test Count:** 20 tests

### Services (xUnit + Moq Tests)

#### RenderQueueServiceTests
- Service initialization
- Job queueing (AddJobAsync)
- Job retrieval (GetActiveJobsAsync, GetCompletedJobsAsync, GetFailedJobsAsync)
- Job management (CancelJobAsync, PauseJobAsync, ResumeJobAsync, RetryJobAsync)
- Queue control (StartQueue, StopQueue)
- Queue statistics
- Event firing (QueueStatusChanged)
- Background work item queueing

**Test Count:** 19 tests

#### SettingsServiceTests
- Settings loading from file
- Default settings creation
- Settings persistence
- Settings caching
- Reset to defaults
- Corrupted JSON handling
- NVENC auto-detection
- Thread-safe concurrent saves
- Directory creation
- Settings file path resolution

**Test Count:** 12 tests

### Models (xUnit Tests)

#### RenderJobTests
- Default value initialization
- Unique JobId generation
- File size formatting (B, KB, MB, GB, TB)
- Timecode conversion (various frame rates)
- Two-stage render support
- Retry tracking
- Process information (crash recovery)
- Track selection
- In/Out points
- Timestamp tracking
- Progress tracking
- Render type support
- Render settings storage

**Test Count:** 26 tests

#### AppSettingsTests
- Default value verification
- Property modification
- SVP integration configuration
- Custom executable paths
- Quality preset support
- Codec support (CPU and hardware)
- CRF range support
- Encoding preset support
- RIFE model version support
- RIFE configuration (threads, UHD mode, TTA mode)
- Max concurrent renders
- Auto-start queue
- Notification configuration
- RIFE executable name resolution (Vulkan vs TensorRT)
- Production-ready defaults

**Test Count:** 20 tests

## Total Test Coverage

- **Total Tests:** 114 comprehensive unit tests
- **Components:** 2 Blazor components (37 tests)
- **Services:** 2 services (31 tests)
- **Models:** 2 models (46 tests)

## Test Patterns Used

### AAA Pattern (Arrange, Act, Assert)

All tests follow the AAA pattern for clarity:

```csharp
[Fact]
public void Example_Test()
{
    // Arrange - Setup test data and mocks
    var renderJob = new RenderJob { JobId = Guid.NewGuid() };

    // Act - Execute the operation being tested
    var result = renderJob.GetOutputFileSizeFormatted();

    // Assert - Verify the expected outcome
    result.Should().Be("N/A");
}
```

### Mocking with Moq

Services are mocked to isolate unit tests:

```csharp
var mockQueueService = new Mock<IRenderQueueService>();
mockQueueService.Setup(x => x.GetActiveJobsAsync())
    .ReturnsAsync(new List<RenderJob>());
```

### BUnit Component Testing

Blazor components are tested with BUnit's rendering and assertion capabilities:

```csharp
var component = RenderComponent<RenderQueue>();
component.Should().NotBeNull();
component.Markup.Should().Contain("Render Queue");
```

### FluentAssertions

Tests use FluentAssertions for readable, expressive assertions:

```csharp
renderJob.Status.Should().Be(RenderJobStatus.Completed);
renderJob.ProgressPercentage.Should().BeInRange(0, 100);
retrievedJobs.Should().HaveCount(2);
```

## Key Test Scenarios

### Component Tests

1. **Rendering:** Verifies components render without errors
2. **User Interaction:** Tests button clicks and event callbacks
3. **State Management:** Validates component state changes
4. **Empty States:** Ensures proper handling of empty data
5. **Error States:** Verifies error display and handling
6. **Cleanup:** Tests proper disposal and event unsubscription

### Service Tests

1. **CRUD Operations:** Tests create, read, update, delete functionality
2. **State Transitions:** Validates job status changes
3. **Event Handling:** Verifies events fire correctly
4. **Error Handling:** Tests error paths and recovery
5. **Concurrency:** Validates thread-safe operations
6. **Configuration:** Tests settings load/save operations

### Model Tests

1. **Initialization:** Verifies default values
2. **Data Validation:** Tests property constraints
3. **Calculations:** Validates computed properties
4. **Formatting:** Tests string formatting methods
5. **Edge Cases:** Covers boundary conditions

## Continuous Integration

These tests are designed to run in CI/CD pipelines:

```yaml
# Example GitHub Actions workflow
- name: Run Tests
  run: dotnet test --logger "trx;LogFileName=test-results.trx"

- name: Code Coverage
  run: dotnet test --collect:"XPlat Code Coverage"
```

## Best Practices

1. **Isolation:** Each test is independent and can run in any order
2. **Clarity:** Test names describe what is being tested
3. **Completeness:** Tests cover happy paths, edge cases, and error conditions
4. **Speed:** Tests execute quickly (no external dependencies)
5. **Maintainability:** Tests follow consistent patterns and are easy to update

## Troubleshooting

### Tests Not Discovered

If tests don't appear in Test Explorer:

1. Rebuild the solution
2. Clean the solution and rebuild
3. Restart Visual Studio
4. Check that test project references the main project

### BUnit Component Tests Fail

If BUnit tests fail:

1. Ensure MudBlazor services are registered: `Services.AddMudServices()`
2. Verify all component dependencies are mocked
3. Check for missing event handlers or callbacks

### Mock Setup Issues

If mocks don't work as expected:

1. Verify the interface/method signature matches
2. Ensure `Returns` or `ReturnsAsync` is used appropriately
3. Check that `Times.Once` or `Times.AtLeastOnce` expectations are correct

## Contributing

When adding new tests:

1. Follow the AAA pattern
2. Use descriptive test names
3. Add tests for new features and bug fixes
4. Ensure tests are independent
5. Keep tests focused on a single behavior
6. Use FluentAssertions for readability

## License

Same as the main project.
