# Internal IT Helpdesk & Support Ticketing System

An internal IT support ticketing system built for our Software Quality Assurance
project. Employees submit tickets, technicians work them through a status
workflow, and managers see analytics on backlog, resolution time, and recurring
issues.

**Stack:** ASP.NET Core Web API + EF Core (SQLite) on the backend, React +
TypeScript (Vite) on the frontend. Core domain logic (status workflow and
dashboard metrics) lives in `Helpdesk.Core` and is the single source of truth,
which keeps our test traceability clean.

---

## 1. What to install

Every collaborator needs these two on their own machine:

| Tool | Version | Where | Verify |
|------|---------|-------|--------|
| **.NET SDK** | 8.0 (SDK, not just Runtime) | search ".NET 8 SDK download" (Microsoft) | `dotnet --version` → `8.x` |
| **Node.js** | 20 LTS or newer | https://nodejs.org (LTS) | `node --version` and `npm --version` |

That's it. All the NuGet packages (EF Core, MSTest, etc.) and npm packages
(React, Vite, axios, Vitest) download automatically when you run `restore` /
`install` — you never fetch those by hand.

> Working in a GitHub Codespace instead? Both tools are pre-installed. Skip
> straight to section 3.

### Recommended VS Code extensions

Open the project folder in VS Code and it will prompt you to install these
(they're listed in `.vscode/extensions.json`):

- **C# Dev Kit** (`ms-dotnettools.csdevkit`) — IntelliSense, debugging, test runner
- **GitHub Copilot** (`github.copilot`) — we need Copilot-usage evidence for the report, so enable it
- **SQLite** (`alexcvzz.vscode-sqlite`) — browse the `helpdesk.db` file
- **GitLens** (`eamodio.gitlens`) — cleaner commit history

---

## 2. Project structure

```
Helpdesk.sln
├── src/
│   ├── Helpdesk.Api/     ASP.NET Core Web API (controllers, service, DI, CORS)
│   ├── Helpdesk.Core/    Domain models, enums, TicketWorkflow, TicketMetrics
│   └── Helpdesk.Data/    EF Core DbContext + sample-data seeder
├── client/               React + TypeScript frontend (Vite)
└── tests/
    └── Helpdesk.Tests/   MSTest unit + integration tests
```

---

## 3. First-time setup

Clone the repo and open it in VS Code:

```bash
git clone https://github.com/<org-or-user>/Internal-IT-Helpdesk-Support-Ticketing-System.git
cd Internal-IT-Helpdesk-Support-Ticketing-System
```

Restore backend dependencies and confirm it builds:

```bash
dotnet restore
dotnet build
```

Install frontend dependencies:

```bash
cd client
npm install
cd ..
```

---

## 4. Running the app

The app has two parts that run at the same time, so use **two terminals**.
In VS Code, click the `+` in the terminal panel to open a second one.

**Terminal 1 — backend API:**

```bash
dotnet run --project src/Helpdesk.Api
```

Runs on **http://localhost:5099**. On first run it creates `helpdesk.db`
(SQLite) and seeds sample tickets. API docs (Swagger) are at
http://localhost:5099/swagger.

**Terminal 2 — frontend:**

```bash
cd client
npm run dev
```

Runs on **http://localhost:5173**. Open that in your browser.

> Note: it's `npm run dev`, **not** `npm start` — Vite uses `dev`.
> The Vite dev server proxies `/api` calls to the backend on 5099, so both must
> be running for the app to work. In Codespaces, use the **Ports** tab to open
> the forwarded 5173 URL.

Stop either server with `Ctrl+C`.

---

## 5. Running the tests

**Backend (MSTest):**

```bash
dotnet test
```

Runs all unit and integration tests (workflow rules, metric calculations,
validation, EF persistence).

**Frontend (Vitest + React Testing Library):**

```bash
cd client
npm run test
```

Runs component and validation tests. Add `-- --watch` to re-run on save while
developing.

---

## 6. Git workflow

We work on feature branches and merge to `main` via pull requests — this keeps
`main` stable and gives us clean collaboration evidence for the report.

```bash
git checkout -b feature/your-feature-name
# ... make changes ...
git add .
git commit -m "Describe what you changed"
git push origin feature/your-feature-name
```

Then open a pull request on GitHub for a teammate to review before merging.

**Don't commit generated folders** — `bin/`, `obj/`, `node_modules/`, and
`*.db` are already in `.gitignore`. Run `git status` before committing; you
should only see source files, not thousands of dependency files.

---

## 7. Common issues

| Problem | Fix |
|---------|-----|
| `dotnet: command not found` | Install the .NET 8 **SDK** (not just Runtime) and reopen the terminal |
| Frontend loads but data fails | The backend isn't running — start Terminal 1 |
| Port 5099 or 5173 already in use | Stop the other process, or change the port in `launchSettings.json` / `vite.config.ts` |
| `git ... please tell me who you are` | `git config --global user.name "…"` and `user.email "…"` |
| NuGet/npm restore errors | Check your internet connection, then re-run `dotnet restore` / `npm install` |
