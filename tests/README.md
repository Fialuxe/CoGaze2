# tests/ — Unity-free test runner

The pure C# assemblies (everything with `"noEngineReferences": true`) are compiled here twice:

- `CoGaze.Pure` builds them against **netstandard2.1 / C# 9**, the API and language level Unity 6
  uses, with warnings as errors. If it builds here it builds in Unity.
- `CoGaze.Tests` runs the NUnit tests that Unity also runs in EditMode. The test files stay next to
  the code they test; this project links them.

```
dotnet test tests/CoGaze.Tests
```

CI runs this on every push and pull request (`.github/workflows/ci.yml`). Unity-dependent tests
(EditMode tests touching UnityEngine, PlayMode) run locally with `scripts/run-unity-tests.ps1`.

When you add a pure assembly, add its folder to `CoGaze.Pure.csproj`. When you add a test folder,
add it to `CoGaze.Tests.csproj`.
