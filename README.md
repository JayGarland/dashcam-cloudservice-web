# Dashcam Cloud Service

A distributed system for authenticating dashcam video footage through cryptographic hashing and video verification. This project combines a TypeScript capture client with a C# validator API to provide a tamper-resistant timeline of video evidence.

## 🎯 Project Overview

**Dashcam Cloud Service** solves the problem of video authenticity in insurance claims and legal proceedings. By continuously hashing video frames and uploading cryptographic signatures to the cloud, we create an immutable record that proves video has not been tampered with after the fact.

### Core Components

- **Capture Client** (`apps/capture-client`) — Browser/client-based TypeScript application
  - Captures video frames at configurable intervals
  - Computes perceptual hashes (dHash64) of each frame
  - Stores hash records in IndexedDB
  - Uploads hashes to Supabase cloud storage

- **Validator API** (`services/validator-api`) — ASP.NET Core REST API
  - Verifies authenticity claims about video segments
  - Extracts frames from uploaded video using FFmpeg
  - Matches extracted hashes against stored timeline
  - Returns verdicts: Verified, Suspicious, or Inconclusive

## 🏗️ Architecture

```
┌─────────────────┐
│ Dashcam Device  │
│ (Browser)       │
└────────┬────────┘
         │ Video + Hash Timeline
         ▼
┌──────────────────────┐
│ Supabase Storage     │
│ (Hash Records)       │
└────────┬─────────────┘
         │ Verify Request
         ▼
┌──────────────────────┐
│ Validator API        │
│ (Verification Logic) │
└──────────────────────┘
```

### Key Algorithms

**dHash64 (Difference Hash)**
- Perceptual hash algorithm: 64-bit hash from image content
- Resize to 9×8 grayscale, compute horizontal differences
- Bit-identical implementation across TypeScript and C#
- Resilient to minor compression artifacts

**Hamming Distance**
- Measures similarity between hash values
- Used to cluster similar frames within tolerance windows
- Threshold-based matching for verification logic

## 🚀 Quick Start

### Prerequisites

- **Node.js 18+** — for capture client
- **.NET 8.0+** — for validator API
- **npm** — for package management
- **Supabase account** — for cloud hash storage (optional for local testing)

### Setup

#### Capture Client

```bash
cd apps/capture-client
npm install
npm test  # Run unit tests
```

#### Validator API

```bash
cd services/validator-api
dotnet restore
dotnet build
dotnet test
```

## 📁 Project Structure

```
dashcam-cloudservice-web/
├── apps/
│   ├── capture-client/          # TypeScript capture & hash client
│   │   ├── src/
│   │   │   ├── capture/         # Frame capture logic
│   │   │   ├── hash/            # dHash64 implementation
│   │   │   ├── storage/         # IndexedDB queue
│   │   │   ├── supabase/        # Cloud API integration
│   │   │   ├── __tests__/       # Unit tests
│   │   │   ├── models.ts        # Shared TypeScript interfaces
│   │   │   └── constants.ts     # Configuration constants
│   │   ├── package.json
│   │   ├── tsconfig.json
│   │   └── vitest.config.ts
│   │
│   └── validator-portal/        # Angular UI (future)
│
├── services/
│   └── validator-api/           # .NET Core validator service
│       ├── Controllers/         # REST endpoints
│       ├── Models/              # Data contracts
│       ├── Services/            # Core verification logic
│       ├── Tests/               # Unit & integration tests
│       └── validator-api.csproj
│
├── docs/                        # Documentation
│   ├── planV1.md               # Development roadmap
│   ├── specV0.md               # Technical specification
│   ├── slice1-log.md           # Slice 1 progress
│   ├── slice2-log.md           # Slice 2 progress
│   ├── slice3-log.md           # Slice 3 progress
│   └── jan23_advancements_group_jie_kusuma.ipynb
│
├── ProjectState.md              # Current project status
├── README.md                    # This file
└── dashcam-cloudservice-web.sln # Visual Studio solution
```

## 🧪 Testing

### TypeScript Tests

```bash
cd apps/capture-client
npm test
```

Tests use **Vitest** framework with deterministic fixtures for hash validation:
- `dhash64.spec.ts` — Validates dHash64 implementation against known images
- `hashQueue.spec.ts` — Tests IndexedDB queue operations
- `sampler.spec.ts` — Tests frame sampling logic
- `uploader.spec.ts` — Tests hash upload workflow

### C# Tests

```bash
cd services/validator-api
dotnet test
```

Unit tests validate:
- `DHash64Tests.cs` — Cross-runtime hash consistency
- `HashMatcherTests.cs` — Hamming distance and matching logic
- `VerificationServiceTests.cs` — End-to-end verification flow

## 📊 Development Status

**Current Slice: Slice 3** ✅

| Slice | Scope | Status |
|-------|-------|--------|
| **1** | dHash64 + Hamming distance core + tests | ✅ Complete |
| **2** | Frame sampling + IndexedDB queue + Supabase upload | ✅ Complete |
| **3** | Verification API + claim validation + matching logic | ✅ Complete |
| **4** | FFmpeg extraction + real Supabase + portal UI + retention | 📋 Planned |

See [ProjectState.md](ProjectState.md) for detailed status and [docs/](docs/) for slice logs.

## 🔧 Core Interfaces

### TypeScript (`models.ts`)

```typescript
export interface CaptureSession {
  sessionId: string;
  deviceClockStartEpochMs: number;
  samplingIntervalMs: number;
  algoVersion: AlgoVersion;
  clientVersion?: string;
}

export interface FrameHashRecord {
  sessionId: string;
  sampleIndex: number;
  elapsedMs: number;
  sampleTimestampEpochMs: number;
  hashHex: string;
  intervalMs: number;
  algoVersion: AlgoVersion;
  createdAtEpochMs: number;
  uploadState: "pending" | "uploaded";
}
```

### C# (Validator API)

```csharp
public class ClaimVerificationRequest {
    public string SessionId { get; set; }
    public string VideoUrl { get; set; }
    public long StartTimestampEpochMs { get; set; }
    public long EndTimestampEpochMs { get; set; }
}

public class VerificationResult {
    public string Verdict { get; set; } // Verified, Suspicious, Inconclusive
    public int ConfidenceScore { get; set; }
    public string[] MatchedHashes { get; set; }
}
```

## 📝 Configuration

### Environment Variables

**Supabase (capture-client)**
```env
VITE_SUPABASE_URL=https://your-project.supabase.co
VITE_SUPABASE_KEY=your-anon-key
```

**Validator API (.NET)**
```env
SUPABASE_URL=https://your-project.supabase.co
SUPABASE_KEY=your-service-role-key
```

## 🔄 API Endpoints

### POST `/api/claims/verify`

Verify authenticity of a video segment.

**Request:**
```json
{
  "sessionId": "session-123",
  "videoUrl": "https://storage.example.com/video.mp4",
  "startTimestampEpochMs": 1700000000000,
  "endTimestampEpochMs": 1700060000000
}
```

**Response:**
```json
{
  "verdict": "Verified",
  "confidenceScore": 98,
  "matchedHashes": ["abc123def456...", "def456ghi789..."],
  "details": "All sampled frames matched timeline with hamming distance < 5"
}
```

## 🛠️ Common Tasks

### Run Capture Client Tests
```bash
cd apps/capture-client && npm test
```

### Build Validator API
```bash
cd services/validator-api && dotnet build
```

### Run Validator API Tests
```bash
cd services/validator-api && dotnet test
```

### Check Hash Consistency
Both implementations use the same dHash64 algorithm. Run tests in both projects to verify cross-runtime consistency:
```bash
npm test -w capture-client
dotnet test services/validator-api
```

## 📚 Documentation

- [Technical Specification](docs/specV0.md) — Detailed algorithm specs and data formats
- [Development Plan](docs/planV1.md) — Roadmap and slicing strategy
- [Project State](ProjectState.md) — Current progress and next steps
- Slice Logs — [1](docs/slice1-log.md), [2](docs/slice2-log.md), [3](docs/slice3-log.md)

## 🤝 Contributing

1. Follow the slice-based development approach outlined in `planV1.md`
2. Ensure cross-runtime hash consistency for any algorithm changes
3. Add tests for new features before implementation
4. Update ProjectState.md with progress

## 📄 License

[Add your license information here]

## 👥 Team

- **Project Lead:** [Your Name]
- **Contributors:** See Git history

---

**Last Updated:** January 2026  
**Current Slice:** 3 (Verification API + Claim Validation)  
**Next Milestone:** Slice 4 (FFmpeg Integration + Portal UI)
