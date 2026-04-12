# LeadManagementSystem

ASP.NET Core MVC (.NET 8) — Hotel Lead Management System  
Authentication phase: Session-based login/register with SHA256, role-based guard, PostgreSQL via Npgsql.

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [PostgreSQL 14+](https://www.postgresql.org/download/)

---

## Setup Steps

### 1. Create the Database

```sql
CREATE DATABASE lead_management_db;
```

### 2. Run the Schema

Connect to your database and run the schema file:

```bash
psql -U postgres -d lead_management_db -f schema.sql
```

This creates the `users` table.

### 3. Configure Connection String

Edit `appsettings.json`:

```json
"ConnectionStrings": {
  "DefaultConnection": "Host=localhost;Port=5432;Database=lead_management_db;Username=postgres;Password=YOUR_PASSWORD"
}
```

### 4. Restore & Run

```bash
cd LeadManagementSystem
dotnet restore
dotnet run
```

Open: `https://localhost:5001` or `http://localhost:5000`

---

## Default Credentials

Register a new account via `/Auth/Register`, then manually promote to Admin in the DB:

```sql
UPDATE users SET role = 'Admin' WHERE email = 'your@email.com';
```

Or use the psql extension to generate the correct SHA256 hash for seeded users:

```sql
-- Requires pgcrypto extension
CREATE EXTENSION IF NOT EXISTS pgcrypto;
UPDATE users SET password = encode(digest('Admin@123', 'sha256'), 'hex')
WHERE email = 'admin@hotel.com';
```

---

## Project Structure

```
LeadManagementSystem/
├── Controllers/
│   ├── AuthController.cs        # Login, Register, Logout, AccessDenied
│   └── DashboardController.cs   # Protected dashboard
├── Data/
│   └── DbHelper.cs              # Raw ADO.NET Npgsql wrapper
├── Filters/
│   └── SessionAuthAttribute.cs  # [SessionAuth] / [SessionAuth("Admin")]
├── Helpers/
│   ├── PasswordHelper.cs        # SHA256 hashing
│   └── SessionHelper.cs        # Session read/write
├── Models/
│   └── UserModels.cs            # User, LoginViewModel, RegisterViewModel
├── Views/
│   ├── Auth/
│   │   ├── Login.cshtml
│   │   ├── Register.cshtml
│   │   └── AccessDenied.cshtml
│   ├── Dashboard/
│   │   └── Index.cshtml
│   └── Shared/
│       └── _Layout.cshtml       # Bootstrap 5 admin sidebar layout
├── wwwroot/css/
│   └── site.css
├── appsettings.json
├── Program.cs
├── schema.sql
└── README.md
```

---

## Auth Flow

| Route | Description |
|---|---|
| `GET /Auth/Login` | Login page |
| `POST /Auth/Login` | Validates credentials, sets session |
| `GET /Auth/Register` | Registration page |
| `POST /Auth/Register` | Creates user (role=User) |
| `GET /Auth/Logout` | Clears session, redirects to login |
| `GET /Auth/AccessDenied` | Shown when role check fails |
| `GET /Dashboard` | Protected — requires login |

## Role Guard Usage

```csharp
[SessionAuth]              // Any logged-in user
[SessionAuth("Admin")]     // Admin only
```

---

## Next Steps (Full Build)

- Master (Status / Module / Product / Category)
- Inquiry CRUD
- Client CRUD + Excel export
- Payment/Income with conditional payment modes
- Expense CRUD + file upload
- User Management (Admin only)
