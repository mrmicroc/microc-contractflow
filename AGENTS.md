# MICROC ContractFlow — agent notes

## Testing discipline
- Every new piece of functionality (controller action, service method, or pure
  frontend logic) ships with at least one test in the same change.
- Backend: integration tests in `src/server/ContractFlow.Api.Tests/` using
  `WebApplicationFactory` + SQLite.
- Frontend: unit tests for pure logic extracted into `src/client/src/lib/`
  (Vitest).
- Every commercial/contract rule gets unit tests built on the worked examples
  in `docs/FUNCTIONAL_DESIGN.md` §5.
- Authorization/role checks always test both the allowed and forbidden path.

## No company names in code
- Never write a company's or customer's name — MICROC's clients, suppliers,
  grid owners — as fixed text anywhere: UI text, help texts, messages,
  defaults, examples, comments or test data. Use neutral examples
  (*Example Org*, *North Link*). Real names are data entered by admins.
- *MICROC* and *ContractFlow* are the product names and may appear.
- Real contract values (rates, baselines) are never seeded — entered as
  configuration by an admin.

## File encoding
All source files are UTF-8. Do not append to files with Windows PowerShell
`Add-Content` / `Set-Content` / `Out-File` without `-Encoding utf8`.

## Repository layout
- Backend: `src/server/ContractFlow.Api/` (http://localhost:5190)
- Backend tests: `src/server/ContractFlow.Api.Tests/`
- Frontend: `src/client/src/` (http://localhost:5175, Vite proxy `/api` → 5190)
- Frontend tests: co-located `*.test.ts` files in `src/client/src/lib/`

## Verification commands (run before every commit)
```
cd src/client && npm run build && npx vitest run
cd src/server && dotnet build && dotnet test
```

## Test counts (update after adding tests)
- Backend: 407
- Frontend: 202 (25 test files)

## Gotchas
- A running local backend (`dotnet run`) locks `ContractFlow.Api\bin` — stop it
  before `dotnet build` / `dotnet test`.
- ImageSharp: 3.x carries known advisories; 4.x requires a Six Labors license
  key for Release builds. No image library is referenced yet — the choice is
  deferred to Phase 2 (receipts). Evaluate SkiaSharp / Magick.NET / licensed
  ImageSharp then.
- "Today" comes from the injected `TimeProvider` (Stockholm local date via
  `LocalClock`). A month can only close once it has ended — tests pin
  `factory.Clock.Set(...)` and reset it, or seed a `ProjectMonthClose` row.
- The dev identity is registered only in Development/Testing; any other
  environment answers 401 everywhere until Entra lands.
- Dev identity travels in the `X-Dev-User-Id` header (`DevCurrentUser`); without
  it the API falls back to the first active Administrator. When Entra sign-in
  lands, `ICurrentUser` is the seam to re-implement from token claims.
