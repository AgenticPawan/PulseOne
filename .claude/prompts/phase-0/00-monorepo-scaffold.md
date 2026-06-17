# Prompt: Monorepo Scaffold

## Context
You are implementing Phase 0 of PulseOne — the foundational monorepo structure. Nothing else can be built until this scaffold exists.

## Task
Create the complete monorepo directory structure and solution file:

```
pulseone-enterprise-solution/
├── .github/workflows/
│   ├── api-producer-deploy.yml
│   ├── worker-consumer-deploy.yml
│   └── security-gates.yml
├── e2e-tests/specs/
├── src/
│   ├── host-admin-app/          # Angular 20 workspace
│   ├── client-app/              # Angular 20 workspace
│   └── backend/
│       ├── PulseOne.sln
│       ├── PulseOne.SharedKernel/
│       ├── PulseOne.CoreDomain/
│       ├── PulseOne.Application/
│       ├── PulseOne.Infrastructure/
│       ├── PulseOne.WebApi/
│       ├── PulseOne.BackgroundWorker/
│       └── PulseOne.MigrationRunner/
```

## Deliverables
1. `PulseOne.sln` referencing all 7 .NET projects
2. Each `.csproj` with correct SDK, target framework (`net10.0`), nullable enabled, and project references
3. `Directory.Build.props` at `src/backend/` with shared properties:
   - `<Nullable>enable</Nullable>`
   - `<ImplicitUsings>enable</ImplicitUsings>`
   - `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
4. `global.json` pinning .NET 10 SDK
5. `.gitignore` appropriate for .NET + Angular + Azure
6. `angular.json` workspace configs for both Angular apps (Angular 20, zoneless)

## Constraints
- `PulseOne.SharedKernel` has NO project dependencies (it is the base)
- Dependency graph: SharedKernel ← CoreDomain ← Application ← Infrastructure ← WebApi/BackgroundWorker/MigrationRunner
- Both Angular apps use `ng new --standalone --style=scss --routing`
- No `AppModule` — all Angular is standalone components with `provideZonelessChangeDetection()`
