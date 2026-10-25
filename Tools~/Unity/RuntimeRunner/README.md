# Diagnostic Player runner (UTF 1.6)

This is a temporary diagnostic path for the macOS Player test-scene deserialization failure recorded in `docs/UnityRegression-2026-10-25.md`. It executes the original 43 `EjoyFramework.Tests.PlayMode` cases through Unity Test Framework 1.6, including coroutine handling and NUnit assertions. It creates the UTF controller at runtime rather than serializing its settings into the build scene. It uses UTF private types by reflection and explicitly retains their linker metadata; this is not a supported public UTF integration or evidence that the default Player test launcher works.

Copy this directory into `Assets/RuntimeRunner` of an isolated Unity project referencing the package with `testables: ["com.ejoy.framework"]`. Use the same package dependencies and Editor version. Do not copy a Library directory. Generated scenes, builds, logs and XML belong under the host's ignored TestResults directory.

```sh
"$EDITOR" -batchmode -quit -projectPath "$HOST" \
  -executeMethod RegressionBuild.Build -regressionBackend Mono2x \
  -regressionOutput "$OUTPUT/Regression-Mono.app" \
  -logFile "$OUTPUT/Regression-Mono-build.log"
# Use the executable name inside the built app's Contents/MacOS directory.
"$OUTPUT/Regression-Mono.app/Contents/MacOS/<host-product-name>" -batchmode \
  -regressionResults "$OUTPUT/Regression-Mono.xml" \
  -logFile "$OUTPUT/Regression-Mono.player.log"
```

For IL2CPP, install the matching Mac IL2CPP module, use `-regressionBackend IL2CPP`, and choose a separate output app and logs. The bootstrap exits 0 only when all 43 expected tests pass with zero failures; it times out after 120 seconds. The NUnit XML root is a test-suite, rather than the default CLI test-run wrapper. Any test count change requires deliberate review of the expected count. Source code, Test Runner version, backend, architecture and logs must be recorded with results.
