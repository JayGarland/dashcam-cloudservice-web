(base) PS F:\GitHub\dashcam-cloudservice-web\services\validator-api> dotnet test .\Tests\validator-api.Tests.csproj
  Determining projects to restore...
  All projects are up-to-date for restore.
  validator-api -> F:\GitHub\dashcam-cloudservice-web\services\validator
  -api\bin\Debug\net8.0\validator-api.dll
  validator-api.Tests -> F:\GitHub\dashcam-cloudservice-web\services\val
  idator-api\Tests\bin\Debug\net8.0\validator-api.Tests.dll
Test run for F:\GitHub\dashcam-cloudservice-web\services\validator-api\Tests\bin\Debug\net8.0\validator-api.Tests.dll (.NETCoreApp,Version=v8.0)  
VSTest version 17.11.1 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    10, Skipped:     0, Total:    10, Duration: 27 ms - validator-api.Tests.dll (net8.0)

Note: Verification service tests use a solid RGBA fixture (non-canonical). Canonical golden vector remains in ProjectState.md.

(base) PS F:\GitHub\dashcam-cloudservice-web\apps\capture-client> npm test

> test
> vitest run

 RUN  v1.6.1 F:/GitHub/dashcam-cloudservice-web/apps/capture-client

 ✓ src/__tests__/hashQueue.spec.ts (2)
 ✓ src/__tests__/dhash64.spec.ts (4)
 ✓ src/__tests__/uploader.spec.ts (2)
 ✓ src/__tests__/sampler.spec.ts (2)

 Test Files  4 passed (4)
      Tests  10 passed (10)
   Start at  20:39:15
   Duration  1.92s (transform 171ms, setup 0ms, collect 536ms, tests 32ms, environment 1ms, prepare 5.38s)
