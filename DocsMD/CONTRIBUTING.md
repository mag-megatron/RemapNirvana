# Contributing to NirvanaRemap

Thank you for your interest in contributing to NirvanaRemap! This document provides guidelines for code style, testing, and the pull request process.

## Code of Conduct

- Be respectful and constructive in all interactions
- Focus on what is best for the community and the project
- Show empathy towards other community members

## Development Setup

### Prerequisites
1. Install **.NET 9.0 SDK** or higher
2. Install **ViGEm Bus Driver** (for testing)
3. Recommended IDE: **Visual Studio 2022** or **JetBrains Rider**

### Clone and Build
```powershell
git clone <repository-url>
cd RemapNirvana
dotnet restore
dotnet build
```

### Run Tests
```powershell
dotnet test
```

---

## Code Style Guide

### General Principles
- Follow **C# Coding Conventions** ([Microsoft Guidelines](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions))
- Use **Clean Architecture** patterns (see [ARCHITECTURE.md](ARCHITECTURE.md))
- Prefer **async/await** for I/O operations
- Enable **nullable reference types** (`#nullable enable`)

### Naming Conventions

| Element | Convention | Example |
|:--------|:-----------|:--------|
| Namespace | PascalCase | `AvaloniaUI.Services` |
| Class | PascalCase | `GamepadRemapService` |
| Interface | PascalCase with `I` prefix | `IGamepadService` |
| Method | PascalCase | `StartListening()` |
| Private Field | camelCase with `_` prefix | `_pad`, `_initialized` |
| Parameter | camelCase | `cancellationToken` |
| Constant | PascalCase | `PollIntervalMs` |

### Formatting
- **Indentation**: 4 spaces (no tabs)
- **Line Length**: Aim for 120 characters max
- **Braces**: Always use braces for control structures, even single-line statements

```csharp
// ✅ Good
if (condition)
{
    DoSomething();
}

// ❌ Bad
if (condition)
    DoSomething();
```

### Comments and Documentation

#### XML Documentation (Required for Public APIs)
All public classes, methods, and properties **must** have XML documentation:

```csharp
/// <summary>
/// Starts listening for gamepad inputs and raises the InputBatch event.
/// </summary>
/// <exception cref="InvalidOperationException">
/// Thrown when SDL initialization fails.
/// </exception>
public void StartAsync()
{
    // Implementation...
}
```

#### Inline Comments
- Use `//` for short explanations
- Focus on **why**, not **what**
- Avoid obvious comments

```csharp
// ✅ Good - explains intent
// Prefer VADER4 over generic controllers for better latency
if (vendor == 0x04B4 && product == 0x2412)
    return 0;

// ❌ Bad - states the obvious
// Check if vendor is 04B4
if (vendor == 0x04B4 && product == 0x2412)
    return 0;
```

---

## Architecture Guidelines

### Dependency Rules
- **Core** layer: No dependencies on other layers
- **Infrastructure** layer: Depends only on Core
- **ApplicationLayer**: Depends only on Core
- **Avalonia** (Presentation): Can depend on all layers

### Interface Design
- Define interfaces in the **Core** layer
- Implement interfaces in the **Infrastructure** or **Avalonia** layers
- Use dependency injection to wire up implementations

### Error Handling
- **Never swallow exceptions** without logging
- Use specific exception types (`InvalidOperationException`, `ArgumentNullException`)
- Document exceptions in XML comments

```csharp
/// <exception cref="InvalidOperationException">
/// Thrown when ViGEm driver is not installed.
/// </exception>
public void Connect() { /* ... */ }
```

---

## Testing Guidelines

### Unit Testing
- Use **xUnit** for test framework
- Mock dependencies with **Moq** or similar
- Follow **AAA pattern**: Arrange, Act, Assert

```csharp
[Fact]
public void BuildOutput_WhenButtonPressed_SetsOutputToOne()
{
    // Arrange
    var engine = new MappingEngine(mockStore.Object);
    var snapshot = new Dictionary<string, double> { ["A"] = 1.0 };
    
    // Act
    var output = engine.BuildOutput(snapshot);
    
    // Assert
    Assert.Equal(1f, output["ButtonA"]);
}
```

### Integration Testing
- Test critical paths (SDL → Mapping → ViGEm)
- Use real SDL/ViGEm when possible, mocks when necessary
- Mark long-running tests with `[Trait("Category", "Integration")]`

### Test Coverage
- Aim for **80% code coverage** on Core and ApplicationLayer
- UI testing is optional but encouraged for critical flows

---

## Pull Request Process

### Before Opening a PR

1. **Create an issue** first to discuss the feature/fix
2. **Fork the repository** and create a feature branch:
   ```bash
   git checkout -b feature/your-feature-name
   ```
3. **Write tests** for your changes
4. **Run all tests** and ensure they pass:
   ```bash
   dotnet test
   ```
5. **Update documentation** (README, ARCHITECTURE) if needed

### PR Title Format
Use [Conventional Commits](https://www.conventionalcommits.org/):

```
feat: add support for PS5 DualSense controllers
fix: resolve SDL memory leak on device disconnect
docs: update ARCHITECTURE.md with new diagrams
refactor: simplify MappingEngine.BuildOutput logic
test: add integration tests for ViGEmOutput
```

### PR Description Template
```markdown
## Description
Brief summary of the change.

## Related Issue
Fixes #123

## Type of Change
- [ ] Bug fix
- [ ] New feature
- [ ] Breaking change
- [ ] Documentation update

## Testing
- [ ] Unit tests added/updated
- [ ] Integration tests added/updated
- [ ] Manual testing performed

## Checklist
- [ ] Code follows project style guidelines
- [ ] Self-review completed
- [ ] Documentation updated
- [ ] No new warnings introduced
```

### Review Process
1. At least **one maintainer approval** required
2. All **CI checks must pass** (build, tests, linting)
3. Address review feedback promptly
4. Squash commits before merging (maintainer will do this)

---

## Commit Guidelines

### Commit Message Format
```
<type>(<scope>): <short summary>

<optional detailed description>

<optional footer>
```

**Example:**
```
fix(SDL): prevent crash when no gamepad is detected

Previously, OpenFirstPad would throw NullReferenceException
when SDL_GetGamepads returned null. Now we check for null
and log a warning instead.

Fixes #45
```

### Types
- `feat`: New feature
- `fix`: Bug fix
- `refactor`: Code restructure without behavior change
- `docs`: Documentation changes
- `test`: Test additions/modifications
- `chore`: Build process, tooling, dependencies

---

## Code Review Checklist

Reviewers should verify:

- [ ] Code adheres to style guide
- [ ] XML documentation present for public APIs
- [ ] Tests cover new/changed code
- [ ] No unnecessary dependencies introduced
- [ ] Error handling is appropriate
- [ ] Performance impact considered
- [ ] Backwards compatibility maintained (or breaking change documented)

---

## Branching Strategy

- **main**: Production-ready code (protected)
- **develop**: Integration branch for features
- **feature/***  Feature branches
- **fix/***: Bug fix branches

### Workflow
1. Create feature branch from `develop`
2. Implement and test your changes
3. Open PR to merge into `develop`
4. After approval, maintainer merges to `develop`
5. Periodically, `develop` is merged to `main` for releases

---

## Debugging Tips

### Enable Verbose Logging
Set environment variable before running:
```powershell
$env:NIRVANA_LOG_LEVEL="DEBUG"
dotnet run --project Avalonia
```

### Inspect SDL Device Info
Check logs at:
```
%APPDATA%/NirvanaRemap/nirvana-input_main.log
```

### Test ViGEm Without SDL
Create a minimal test:
```csharp
var vigem = new ViGEmOutput();
vigem.Connect();
vigem.ApplyAll(new Dictionary<string, float> { ["ButtonA"] = 1f });
// Check Windows Game Controller settings to see virtual controller
```

---

## Release Process (Maintainers Only)

1. Update version in `Directory.Build.props`
2. Update [file:///c:/Users/Gavv/Documents/Antigravity/RemapNirvana/README.md#L88-L88](CHANGELOG.md)
3. Create release tag: `git tag v1.2.0`
4. Build release binaries: `dotnet publish -c Release`
5. Create GitHub release with binaries attached

---

## Getting Help

- **Questions?** Open a GitHub Discussion
- **Bug reports:** Create an issue with reproduction steps
- **Feature requests:** Open an issue with use case description

---

## License

By contributing, you agree that your contributions will be licensed under the same license as the project (see LICENSE file).

---

Thank you for contributing to NirvanaRemap! 🎮
