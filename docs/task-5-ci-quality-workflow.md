# Task 5 — Continuous Integration and DevOps QA

## Workflow

The repository uses GitHub Actions through [`.github/workflows/quality-checks.yml`](../.github/workflows/quality-checks.yml). It runs on pushes to `main`, pull requests targeting `main`, and manual dispatch. Backend and client checks run as independent Ubuntu jobs. The workflow requests read-only repository contents permission and cancels older runs for the same ref when a newer run starts.

## Quality gates

| Gate | Automated check | Pass condition |
|---|---|---|
| Backend build and regression | `dotnet test Helpdesk.slnx --configuration Release` | Solution builds and every discovered backend test passes. |
| API smoke/sanity | Start the built API, then request `GET /api/technicians` | API starts within 60 seconds and returns a JSON array with HTTP success. |
| Client dependency integrity | `npm ci` using `client/package-lock.json` | Locked dependencies install successfully. |
| Client build/type check | `npm run build` | TypeScript project build and Vite production build both succeed. |
| Client lint | `npm run lint` | ESLint exits successfully. Existing hook dependency warnings are visible in logs but do not currently fail the gate. |

Any failing step fails its job and therefore the workflow run. The API uses its normal startup seeding against the job's disposable runner filesystem; no production secrets or external database are required.

## Evidence and current status

The local backend test command passed **24/24 tests**. The client production build passed, and lint exited successfully with three existing React Hook dependency warnings. An API was started locally and `GET /api/technicians` returned HTTP 200. These results validate the individual checks locally; the GitHub-hosted workflow has not yet run, so a GitHub Actions run URL/result is pending the next push or manual dispatch.

## Limitations and follow-up

- The client package currently defines no test script and contains no Vitest test files. The CI workflow does not represent an empty client test run as passing coverage; the frontend gates currently cover build/type-check and lint only. Add meaningful UI tests and a `test` script in a follow-up.
- Lint currently allows three React Hook dependency warnings. Resolve them and consider promoting warnings to errors after confirming the intended hook dependencies.
- The smoke check verifies startup and one API route, not full authentication, persistence, or the end-to-end technician journey.
- The separate Task 3 test report records failed anonymous reporter-data and internal-note visibility checks. This CI workflow does not yet run those checks as regression tests and must not be interpreted as evidence that those security issues are fixed.
- Add explicit coverage thresholds, accessibility checks, and performance budgets once their baselines and acceptable limits are agreed.
