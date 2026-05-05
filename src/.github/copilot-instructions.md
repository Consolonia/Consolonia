# Copilot Instructions

## General Guidelines
- First general instruction
- Second general instruction

## Code Style
- Use specific formatting rules
- Follow naming conventions

## Project-Specific Rules
- When creating benchmarks for Consolonia (or any project on a non-system drive), always configure the BenchmarkDotNet artifacts path to be on the same drive as the project to avoid cross-volume move errors from the VSDiagnostics collector. Use: `DefaultConfig.Instance.WithArtifactsPath(Path.Combine(AppContext.BaseDirectory, "BenchmarkArtifacts"))`