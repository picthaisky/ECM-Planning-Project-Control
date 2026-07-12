# Tech Stack Reference

Source: docs/2.–4. Update here (via knowledge-curator) when versions or choices change.

## Backend
| Item | Choice | Notes |
| --- | --- | --- |
| Runtime | C# .NET 10 Web API | Clean Architecture, 4 projects (ADR-0001) |
| ORM | EF Core 10 + MSSQL Server | code-first migrations; global tenant filters (ADR-0002) |
| CQRS | MediatR + FluentValidation | pipeline: validation → tenant/authz → audit |
| Parsers | native XER parser · MPXJ.Net (MSPDI) · EPPlus (xlsx) | ADR-0003; parsers are untrusted-input surfaces |
| Auth | JWT + RBAC | roles: PM, Planning, Site, QS, Executive, Admin |
| Files | Azure Blob / S3 | photos compressed client-side before upload |

## Frontend
| Item | Choice | Notes |
| --- | --- | --- |
| Framework | React 19 + TypeScript (strict) + Vite | module-based `src/features/<module>/` |
| Styling | Tailwind CSS, custom warm orange-brown theme | tokens in `/cmplus-ui` skill; no raw hex in components |
| State | Zustand (UI) + React Query (server cache) | never mirror server data in Zustand |
| PWA | Service Worker + IndexedDB outbox + Background Sync | ADR-0005 |
| Perf | react-window virtualization, Web Workers, canvas Gantt layer | ADR-0004 |

## Testing & delivery
| Item | Choice |
| --- | --- |
| Backend tests | xUnit (unit + integration vs test DB) |
| Frontend tests | Vitest + Testing Library |
| E2E | Playwright (incl. offline→sync flows) |
| CI/CD | GitHub Actions → Docker → AWS ECS / Azure Container Apps, gated promotion |

## Performance budgets
- WBS tree API: **< 100 ms**
- Gantt: smooth at **10,000+ activities**
- Site photo stored size: **≤ ~300 KB** after client compression
