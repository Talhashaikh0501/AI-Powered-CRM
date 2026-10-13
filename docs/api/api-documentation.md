# AI-Powered CRM - REST API Documentation (v1)

**Base Path:** `/api/v1`  
**Authentication:** Cookie-based session / Bearer Token compatible  
**Content-Type:** `application/json`

---

## 1. Leads Endpoints

### `GET /api/v1/leads`
Query leads with filtering, search, and pagination.
* **Query Parameters:**
  * `search` (string, optional): Search across name, phone, email, company, and product.
  * `status` (string, optional): Filter by `LeadStatus` (`New`, `Contacted`, `Interested`, `Qualified`, `Proposal`, `Negotiation`, `Converted`, `Lost`).
  * `priority` (string, optional): `Low`, `Medium`, `High`.
  * `source` (string, optional): Filter by acquisition channel.
  * `page` (int, default: 1): Page number.
  * `pageSize` (int, default: 15): Number of items per page.
* **Response (200 OK):**
```json
{
  "total": 7,
  "page": 1,
  "pageSize": 15,
  "data": [
    {
      "leadId": 1,
      "name": "Ahmed Khan",
      "phone": "+91-9876543210",
      "email": "ahmed@example.com",
      "company": "AK Global Traders",
      "source": "Instagram",
      "interestedProduct": "Full-Stack Custom ERP System",
      "budget": 85000.00,
      "status": "Qualified",
      "priority": "High"
    }
  ]
}
```

### `GET /api/v1/leads/{id}`
Retrieve complete lead profile including activities, follow-ups, and AI insights.
* **Response (200 OK):** Lead entity with child navigation records.

### `POST /api/v1/leads`
Create a new prospect.
* **Request Body:**
```json
{
  "name": "Ahmed Khan",
  "phone": "+91-9876543210",
  "email": "ahmed@example.com",
  "company": "AK Global Traders",
  "source": "Instagram",
  "interestedProduct": "Full-Stack Custom ERP System",
  "budget": 85000,
  "status": "New",
  "priority": "High"
}
```
* **Response (201 Created):**
```json
{
  "leadId": 15,
  "status": "New",
  "message": "Lead created successfully"
}
```

### `PUT /api/v1/leads/{id}`
Update an existing prospect's parameters.

### `POST /api/v1/leads/{id}/archive`
Soft-archive a lead (Admin & Manager only).

### `POST /api/v1/leads/{id}/assign`
Reassign lead ownership to another staff member.

---

## 2. AI Intelligence Endpoints

### `POST /api/v1/ai/leads/{id}/analyze`
Triggers real-time LLM inference (via Groq `openai/gpt-oss-20b` or Deterministic Fallback Scorer).
* **Response (200 OK):**
```json
{
  "insightId": 101,
  "leadId": 15,
  "classification": "Hot",
  "score": 92,
  "summary": "High-intent enterprise lead with confirmed executive buy-in and $85k budget fit.",
  "nextBestAction": "Dispatch master services agreement and schedule onboarding call tomorrow at 11 AM.",
  "riskLevel": "Low",
  "urgency": "High",
  "reasons": [
    "High-value budget fit ($85,000)",
    "Active client engagement with 3+ touchpoints logged",
    "Scheduled follow-up confirmed on calendar"
  ],
  "suggestedResponse": "Hi Ahmed, thank you for your time on our demonstration call. I have prepared our proposal as discussed...",
  "provider": "Groq",
  "model": "openai/gpt-oss-20b",
  "generatedAt": "2026-10-13T09:15:00Z"
}
```

### `POST /api/v1/ai/leads/{id}/draft-response`
Generates a customized follow-up communication draft tailored to the lead's historical interactions.
* **Request Body:**
```json
{
  "leadId": 15,
  "userTone": "Professional and persuasive",
  "customInstructions": "Offer a 5% discount if signed before Friday"
}
```
* **Response (200 OK):**
```json
{
  "draft": "Hi Ahmed, following up on our proposal discussion..."
}
```

---

## 3. Follow-Up Queues Endpoints

* `GET /api/v1/followups` (Query pending, overdue, or completed queues)
* `POST /api/v1/leads/{id}/followups` (Schedule a new follow-up action)
* `PUT /api/v1/followups/{id}` (Reschedule follow-up to new due date)
* `POST /api/v1/followups/{id}/complete` (Record outcome notes and complete task)

---

## 4. Analytics & Management Endpoints

* `GET /api/v1/dashboard/summary` (Aggregated KPIs, conversion rates, urgent queues)
* `GET /api/v1/reports/conversion` (Funnel drop-off breakdown and win rates)
* `GET /api/v1/reports/sources` (Lead channel conversion rates and revenue realized)
* `GET /api/v1/reports/staff` (Rep leaderboard with closed revenue and conversion %)
