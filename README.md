# AI-Powered CRM — Multi-Tenant Commercial SaaS

A high-performance, multi-tenant AI-Powered Customer Relationship Management (CRM) platform engineered with ASP.NET Core 8 MVC, Entity Framework Core 8, Microsoft SQL Server, and Groq AI. Tailored for high-velocity sales teams with multi-company workspace isolation, automated round-robin lead assignment, AI-assisted lead scoring, and centralized Platform SuperAdmin operations.

---

## 🏛️ System Architecture

- **Framework:** .NET 8.0 (ASP.NET Core MVC)
- **Database:** Microsoft SQL Server (Shared database `AITStudentDB` with strict `TalhaCRM_` object prefix isolation)
- **Authentication & Security:** ASP.NET Core Identity, BCrypt/PBKDF2 hashing, Anti-CSRF tokens, sliding session expiration, security stamp invalidation, and custom EF Core Global Query Filters (`ITenantEntity`)
- **AI Intelligence Engine:** Groq API (Llama 3 models) with deterministic, rule-based fallback and INR (`₹`) budget scoring
- **Default Currency:** Indian Rupee (`INR` / `₹`) throughout pricing, quotas, budgeting, and platform MRR analytics
- **Frontend Stack:** ASP.NET Core Razor Views, Bootstrap 5, Modern CSS Variables, Responsive Viewports, and Vanilla JavaScript (zero heavy SPA framework dependencies)

---

## 🔐 Platform SuperAdmin Operations & Provisioning

The platform includes a dedicated, secure Platform Operations Console located at `/Platform`.

### Strict Privacy & Tenant Separation
Platform SuperAdmins are system operators who monitor system health, tenant workspaces, and quotas. To preserve enterprise customer privacy:
1. **Zero Tenant Memberships:** Platform SuperAdmin accounts never possess `TenantMembership` records.
2. **Strict Lead Isolation:** SuperAdmins cannot browse or view customer leads directly. Delegated access to customer data is strictly governed by time-bound, customer-approved `SupportAccessTicket` workflows.
3. **Dedicated Redirection:** Upon login, accounts in the `PlatformSuperAdmin` role are automatically routed directly to `/Platform`.

### Provisioning the First Platform SuperAdmin

The system supports automatic, secure bootstrapping of the initial Platform SuperAdmin on first run via configuration or environment variables.

#### Option A: Via Environment Variables (Recommended for Production)
Set the following environment variables before starting the application:
```bash
CRM_SUPERADMIN_EMAIL="superadmin@crm-platform.com"
CRM_SUPERADMIN_PASSWORD="YourSecurePassword!2026"
CRM_SUPERADMIN_FULLNAME="Platform Operations Lead"
```

#### Option B: Via `appsettings.json` or `appsettings.Development.json`
```json
{
  "Security": {
    "SuperAdminBootstrap": {
      "Email": "superadmin@crm-platform.local",
      "Password": "ChangeMe!Platform2026",
      "FullName": "Platform Administrator"
    }
  }
}
```

### Platform Operations Capabilities
Once logged in at `/Platform`, SuperAdmins have access to:
- **Tenant Workspace Directory:** Search by company name, slug, or contact email; filter by subscription tier or active status.
- **Financial & Growth Metrics:** Total tenant workspaces, active business accounts, total platform leads, and Monthly Recurring Revenue (MRR in ₹).
- **Tenant Management Detail (`/Platform/TenantDetails/{id}`):**
  - Quota utilization progress bars (User Seats, Lead Quota, AI Requests).
  - Workspace member directory with role badges and join dates.
  - Platform audit trail tracking administrative interventions.
- **Tenant Suspension / Activation:** One-click instant workspace suspension and restoration.
- **Administrative Tier & Quota Adjustment:** Upgrade or downgrade subscription plans (Free/Trial, Starter, Professional, Enterprise) and modify seat/lead quotas without requiring payment gateway integration.

---

## 📧 Email Verification & Founder Onboarding

- **Founder Registration (`/Account/RegisterCompany`):** New company founders register their workspace and user account. `EmailConfirmed` is initialized to `false`.
- **Email Confirmation Enforcement:** Unconfirmed accounts are blocked at login with a polite notification and an option to resend confirmation emails.
- **Transactional Delivery Service (`IEmailDeliveryService`):**
  - SMTP configuration via `EmailSettings` in `appsettings.json`.
  - Safe development fallback: In local development or testing environments without SMTP, emails are written to structured logs with full token links.
- **Anti-Enumeration Protection:** The resend confirmation endpoint uses a universal success message to prevent user enumeration attacks and is protected by IP rate limiting.

---

## ⚡ Concurrency-Safe Round-Robin Lead Assignment

Lead routing is safeguarded against race conditions in high-volume environments:
- **Per-Tenant Synchronization (`SemaphoreSlim`):** Guarantees serial execution of round-robin rotation per workspace.
- **In-Flight Workload Reservations:** Prevents duplicate assignments when multiple leads are received simultaneously before the database transaction commits.
- **Workload Balancing:** Dynamically evaluates active sales representative queue loads and rotates assignments fairly.

---

## 📋 Bulk Lead Operations & Multi-Tenant Boundaries

The Leads workspace (`/Leads`) provides a dynamic bulk action toolbar:
- **Multi-Select Checkboxes:** Select individual leads or use the select-all toggle.
- **Bulk Assignment:** Reassign selected leads to any active sales representative in the company.
- **Bulk Status Progression:** Advance selected leads across workflow stages (e.g., New -> Contacted -> Qualified).
- **Bulk Archiving:** Move obsolete leads to the archive with automatic activity logging.
- **Tenant Boundary Enforcement:** All bulk actions filter by the active tenant ID and reject cross-tenant IDs silently or with security exceptions, preventing tampering.

---

## 💳 Payment Gateway Status

> **Important Architecture Notice:**
> The entire subscription and billing user interface (Pricing tables, Monthly/Yearly toggle, INR currency formatting, Subscription Management, and Invoicing views) is fully developed, polished, and ready.
> In accordance with project roadmap requirements, online payment gateways (Stripe / Razorpay), automated card billing, refunds, and webhooks are scheduled for the **FINAL DEVELOPMENT PHASE**.
> Plan selections in the UI currently trigger a professional "Online Checkout Coming Soon" modal and allow founders to request administrative upgrades.

---

## 🧪 Automated Test Suite

The test suite contains **120 automated tests** covering multi-tenant isolation, security policies, SQL Server migration safeguards, support access workflows, concurrency safety, email verification, and bulk actions.

To run the complete test suite:
```powershell
powershell -Command "dotnet test"
```

**Latest Test Run:**
```
Passed!  - Failed: 0, Passed: 120, Skipped: 0, Total: 120, Duration: ~13s
```