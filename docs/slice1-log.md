## evidence: slice1-log

    ```shell
    (base) PS F:\GitHub\dashcam-cloudservice-web> cd apps/capture-client; npm test

    > test
    > vitest run

    RUN  v1.6.1 F:/GitHub/dashcam-cloudservice-web/apps/capture-client

    ✓ src/__tests__/dhash64.spec.ts (4)
    ✓ dhash64 (2)
        ✓ should produce expected hex for a deterministic synthetic RGBA fixture
        ✓ bit packing should be LSB-first row-major
    ✓ hammingDistance64 (2)
        ✓ returns 0 for identical hashes
        ✓ counts differing bits

    Test Files  1 passed (1)
        Tests  4 passed (4)
    Start at  23:27:53
    Duration  508ms (transform 32ms, setup 0ms, collect 74ms, tests 3ms, environment 0ms, prepare 168ms)
    ```
