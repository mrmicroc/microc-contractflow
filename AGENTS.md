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

## Repository layout (once scaffolded)
- Backend: `src/server/ContractFlow.Api/`
- Backend tests: `src/server/ContractFlow.Api.Tests/`
- Frontend: `src/client/src/`
- Frontend tests: co-located `*.test.ts` files in `src/client/src/lib/`

## Verification commands (run before every commit)
```
cd src/client && npm run build && npx vitest run
cd src/server && dotnet build && dotnet test
```
