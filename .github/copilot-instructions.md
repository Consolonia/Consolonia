# Consolonia TUI Framework - GitHub Copilot Instructions

> **About this file**: This document provides comprehensive guidance for GitHub Copilot coding agents working on the Consolonia repository. It contains validated build instructions, coding conventions, testing requirements, and troubleshooting tips to help you work effectively with this codebase.

## Overview

Consolonia is a TUI (Text User Interface) implementation for Avalonia UI. It enables building modern, console-based applications using Avalonia's XAML markup, data binding, and declarative patterns while running entirely in the terminal.

**Always reference these instructions first** and fallback to search or bash commands only when you encounter unexpected information that does not match the info here.

### Quick Links
- [Project Wiki](https://github.com/jinek/Consolonia/wiki) - Comprehensive documentation
- [Contributing Guide](../contributing.md) - How to contribute
- [README](../readme.md) - Project overview and quick start

## Working Effectively

### Prerequisites and Setup
- **.NET 8.0 SDK** is required. The project targets `net8.0` framework.
- All commands should be run from the `/src` directory within the repository.
- NEVER CANCEL: Build operations may take 60+ minutes in some environments. Always set timeout values of 90+ minutes for build commands.
- NEVER CANCEL: Test operations may take 30+ minutes. Always set timeout values of 45+ minutes for test commands.

### Build and Test Commands (VALIDATED)
Bootstrap, build, and test the repository:

1. **Restore dependencies** (30s typical):
   ```bash
   cd src
   dotnet restore
   ```

2. **Build solution** - Debug configuration (43s typical):
   ```bash
   cd src
   dotnet build --no-restore
   ```
   - NEVER CANCEL: Set timeout to 90+ minutes minimum
   - Produces warnings in Experimental projects (normal)
   - Produces 1 obsolete theme warning in template (normal)

3. **Build solution** - Release configuration (27s typical):
   ```bash
   cd src
   dotnet build --no-restore --configuration release
   ```
   - NEVER CANCEL: Set timeout to 90+ minutes minimum

4. **Run tests** - Debug configuration (34s typical):
   ```bash
   cd src
   dotnet test
   ```
   - NEVER CANCEL: Set timeout to 45+ minutes minimum
   - Expected result: 148 passed, 1 skipped, 36 passed (Gallery tests)
   - Some tests may be skipped in CI environments

5. **Run tests** - Release configuration (33s typical):
   ```bash
   cd src
   dotnet test --configuration release
   ```
   - NEVER CANCEL: Set timeout to 45+ minutes minimum

### Code Quality and Formatting
**Always run formatting before committing changes:**
```bash
cd src
dotnet format
```
- Takes ~40s to complete
- May show "Unable to fix IDE1006" warnings (normal - naming rule issues require manual fixes)
- The CI build will fail if code is not properly formatted

**Check formatting without applying changes:**
```bash
cd src
dotnet format --verify-no-changes
```
- Exit code 2 indicates formatting issues exist
- Exit code 0 indicates no formatting issues

### Running Applications

#### Example Applications
**Basic Example App** (Console movie catalog):
```bash
cd src/Example
dotnet run
```
- Displays a functional TUI movie database application
- Press Ctrl+C to exit

**Consolonia Gallery** (Component showcase):
```bash
cd src/Consolonia.Gallery
dotnet run
```
- Shows all available Consolonia UI components
- Interactive demonstration of TUI capabilities
- Press Ctrl+C to exit

#### Global Tool Installation and Usage
**Install Consolonia Gallery globally** (5s):
```bash
dotnet tool install -g Consolonia.Gallery --prerelease
```

**Run globally installed Gallery**:
```bash
Consolonia.Gallery
```

**Uninstall global Gallery tool**:
```bash
dotnet tool uninstall -g Consolonia.Gallery
```

#### Project Templates
**Install project template** (4s):
```bash
dotnet new install Consolonia.Templates
```

**Create new Consolonia application**:
```bash
mkdir MyConsoloniaApp
cd MyConsoloniaApp
dotnet new consolonia.app --name MyConsoloniaApp
```

**Run template-generated application**:
```bash
dotnet run
```
- Creates a simple "Welcome to Consolonia!" application
- Contains FluentTheme deprecation warning (normal)

## Validation

### Manual Testing Requirements
**ALWAYS run through at least one complete end-to-end scenario after making changes:**

1. **Template Validation Scenario**:
   - Install templates: `dotnet new install Consolonia.Templates`
   - Create project: `dotnet new consolonia.app --name TestApp`
   - Build and run: `cd TestApp && dotnet run`
   - Verify: Application shows "Welcome to Consolonia!" message
   - Verify: UI is responsive and can be closed with Ctrl+C

2. **Gallery Validation Scenario**:
   - Build Gallery: `cd src/Consolonia.Gallery && dotnet run`
   - Verify: Left panel shows component list (Welcome, TextBlock, Button, etc.)
   - Verify: Can navigate through component list with arrow keys
   - Verify: Right panel shows selected component demonstration
   - Verify: Can interact with different UI components

3. **Example Validation Scenario**:
   - Build Example: `cd src/Example && dotnet run`
   - Verify: Movie catalog interface displays
   - Verify: Can select movies from the list
   - Verify: Movie details appear in right panel
   - Verify: Genre dropdown is functional

### Build Validation Steps
Always perform these validation steps for CI compatibility:
1. `dotnet restore` - must succeed
2. `dotnet build --configuration release` - must succeed with only expected warnings  
3. `dotnet test` - must pass with expected results (148+ passed, minimal skipped)
4. `dotnet format --verify-no-changes` - must return exit code 0

## Repository Structure

### Key Projects
- **`Consolonia.Core`** - Core TUI framework implementation
- **`Consolonia.Themes`** - Default themes and styling
- **`Consolonia.Gallery`** - Component showcase application (packaged as global tool)
- **`Example`** - Sample movie catalog application
- **`Consolonia.Templates`** - dotnet project templates
- **`Consolonia.Modal`** - Modal dialogs and overlay system
- **`Tools/Consolonia.PreviewHost`** - Live AXAML preview tool
- **`Tests/`** - NUnit test projects
- **`Experimental/`** - Experimental features (may have warnings/errors)

### Important Files
- **`src/Consolonia.sln`** - Main solution file
- **`src/.editorconfig`** - Code formatting rules (extensive)
- **`.github/workflows/general_build.yml`** - CI build configuration
- **`.github/workflows/editorconfig.yml`** - Code formatting CI check

## Common Tasks

### Development Workflow
1. Make changes to code
2. Build: `dotnet build`
3. Test: `dotnet test`  
4. Format: `dotnet format`
5. Validate: Run manual testing scenarios
6. Commit changes

### Package Management
**Create NuGet packages**:
```bash
cd src
dotnet pack --configuration release -o ./packages --version-suffix beta.test
```
- Creates packages in `src/packages/` directory
- Includes tools: `Consolonia.Gallery`, `Consolonia.PreviewHost`

### Common Issues and Solutions
- **Build warnings in Experimental projects**: Normal, these are development projects
- **FluentTheme obsolete warnings**: Normal, templates will be updated in future releases
- **Formatting failures**: Run `dotnet format` before committing
- **Test skips in CI**: Expected behavior, some tests require specific environments
- **Long build times**: Normal, set appropriate timeouts and never cancel operations

## Framework Information
- **Base Framework**: Built on Avalonia UI 11.3.4
- **Target Platform**: .NET 8.0
- **UI Technology**: Console-based TUI using XAML markup
- **Cross-Platform**: Windows, macOS, Linux support
- **Architecture**: Uses Avalonia's rendering pipeline optimized for text output

## CI/CD Information
- **Build timeout**: 30 minutes (may need adjustment for complex builds)
- **Test framework**: NUnit with custom Consolonia test base classes
- **Code analysis**: Uses JetBrains ReSharper for style checking
- **Formatting**: EditorConfig + dotnet format integration
- **Publishing**: Automated NuGet package publishing to nuget.org on main branch

Always build and exercise your changes using the validation scenarios above before committing.

## Security Best Practices

When contributing code to Consolonia:

### Critical Security Rules
- **NEVER commit secrets, API keys, passwords, or credentials** to the repository
- **NEVER commit tokens or authentication information** in code, comments, or test files
- **DO NOT introduce known security vulnerabilities** - use secure coding practices
- **DO NOT disable security features** without explicit justification and review

### Secure Coding Practices
- **Validate all user input** - Especially important for TUI applications that parse console input
- **Use secure dependencies** - Keep Avalonia UI and other dependencies up to date
- **Handle exceptions properly** - Don't expose sensitive information in error messages
- **Review third-party packages** - Check for known vulnerabilities before adding dependencies
- **Use secure communication** - If adding network features, use HTTPS/TLS

### Code Review for Security
- All changes go through pull request review
- Security-sensitive changes require extra scrutiny
- If you discover a security vulnerability, report it privately to maintainers before creating a public issue

## Coding Patterns and Conventions

### Architecture Patterns
- **MVVM Pattern**: Consolonia uses the Model-View-ViewModel pattern from Avalonia
  - Views: AXAML files defining UI layout
  - ViewModels: C# classes handling logic and data binding
  - Models: Data structures and business logic
  
- **Control Pattern**: Custom TUI controls inherit from Avalonia controls
  - Override rendering methods for console output
  - Use Avalonia's layout and styling system
  - Respect existing control hierarchy

### File Organization
- **Controls**: `/src/Consolonia.Controls/` - TUI-specific UI controls
- **Core**: `/src/Consolonia.Core/` - Core rendering and platform abstraction
- **Themes**: `/src/Consolonia.Themes/` - Styling and theming
- **Tests**: `/src/Tests/` - NUnit tests organized by project
- **Examples**: `/src/Example/` and `/src/Consolonia.Gallery/` - Sample applications

### Naming Conventions
- Follow C# naming conventions (PascalCase for public members, camelCase for private)
- Use descriptive names that indicate purpose
- AXAML files should match their code-behind class names
- Test classes should end with `Tests` suffix

### Documentation Standards
- **XML Documentation**: Add XML doc comments for public APIs
- **Inline Comments**: Use sparingly, prefer self-documenting code
- **README Updates**: Update relevant documentation when adding features
- **Wiki Updates**: Major features should be documented in the wiki

## Contribution Workflow

### Before Starting Work
1. Check existing issues and discussions
2. For bugs, verify the issue exists in Avalonia first
3. For features, discuss the approach in an issue
4. Review the [contributing guide](../contributing.md)

### Making Changes
1. Create a feature branch from main
2. Make focused, logical commits with clear messages
3. Follow the code style (enforced by .editorconfig)
4. Add or update tests for your changes
5. Run the full build and test suite
6. Format code with `dotnet format`

### Pull Request Process
1. Ensure all validation steps pass
2. Write clear PR description explaining changes
3. Reference related issues
4. Respond to review feedback
5. Squash commits if requested
6. PR will be merged by squashing (commit messages matter!)

## Additional Resources

### External Dependencies
- **Avalonia UI 11.3.4**: Core UI framework - [Documentation](https://docs.avaloniaui.net/)
- **.NET 8.0**: Target framework - [Documentation](https://learn.microsoft.com/en-us/dotnet/)
- **NUnit**: Testing framework - [Documentation](https://docs.nunit.org/)

### Getting Help
- **GitHub Issues**: For bugs and feature requests
- **GitHub Discussions**: For questions and general discussion
- **Wiki**: For comprehensive documentation
- **Contributing Guide**: For contribution guidelines

### Known Limitations
- Some Avalonia features are not yet supported in console rendering
- Performance may vary significantly between terminal emulators
- Windows, macOS, and Linux terminals have different capabilities
- Some tests may be skipped in CI due to environment constraints