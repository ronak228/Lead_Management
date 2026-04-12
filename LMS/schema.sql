-- =============================================
-- LeadManagementSystem – PostgreSQL Schema v2
-- Production-ready, full relationships
-- =============================================

-- USERS
DROP TABLE IF EXISTS users CASCADE;
CREATE TABLE users (
    id          SERIAL PRIMARY KEY,
    full_name   VARCHAR(150) NOT NULL,
    email       VARCHAR(150) NOT NULL UNIQUE,
    password    VARCHAR(256) NOT NULL,
    role        VARCHAR(20)  NOT NULL DEFAULT 'Client' CHECK (role IN ('Admin','Employee','Client')),
    is_active          BOOLEAN      NOT NULL DEFAULT TRUE,
    is_deleted         BOOLEAN      NOT NULL DEFAULT FALSE,   -- soft-delete: preserves FK audit chain
    setup_token        VARCHAR(100) NULL,                     -- one-time account-setup token
    setup_token_expires TIMESTAMP   NULL,                     -- token expiry (7 days)
    created_at         TIMESTAMP    NOT NULL DEFAULT NOW(),
    updated_at         TIMESTAMP    NOT NULL DEFAULT NOW()
);

-- Migration (run on existing DB):
-- ALTER TABLE users ADD COLUMN IF NOT EXISTS setup_token VARCHAR(100) NULL;
-- ALTER TABLE users ADD COLUMN IF NOT EXISTS setup_token_expires TIMESTAMP NULL;

-- CONFIGURATION MASTER TABLES

DROP TABLE IF EXISTS cfg_status CASCADE;
CREATE TABLE cfg_status (
    id         SERIAL PRIMARY KEY,
    name       VARCHAR(100) NOT NULL UNIQUE,
    is_active  BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

DROP TABLE IF EXISTS cfg_module CASCADE;
CREATE TABLE cfg_module (
    id         SERIAL PRIMARY KEY,
    name       VARCHAR(100) NOT NULL UNIQUE,
    is_active  BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

DROP TABLE IF EXISTS cfg_product CASCADE;
CREATE TABLE cfg_product (
    id         SERIAL PRIMARY KEY,
    name       VARCHAR(100) NOT NULL UNIQUE,
    is_active  BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

DROP TABLE IF EXISTS cfg_category CASCADE;
CREATE TABLE cfg_category (
    id         SERIAL PRIMARY KEY,
    name       VARCHAR(100) NOT NULL UNIQUE,
    is_active  BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

DROP TABLE IF EXISTS cfg_city CASCADE;
CREATE TABLE cfg_city (
    id         SERIAL PRIMARY KEY,
    name       VARCHAR(100) NOT NULL UNIQUE,
    is_active  BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

-- =============================================
-- SEED DATA
-- =============================================
INSERT INTO cfg_status (name) VALUES
  ('New'),('Contacted'),('Follow Up'),('Negotiation'),('Converted'),('Lost');

INSERT INTO cfg_module (name) VALUES
  ('Housekeeping'),('Front Office'),('Food & Beverage'),('Banquet'),('Maintenance'),('Accounts');

INSERT INTO cfg_product (name) VALUES
  ('Room Management'),('POS System'),('Booking Engine'),('Payroll'),('Inventory'),('CRM');

INSERT INTO cfg_category (name) VALUES
  ('Office Expense'),('Travel'),('Utilities'),('Marketing'),('Maintenance'),('Salary');

INSERT INTO cfg_city (name) VALUES
  ('Mumbai'),('Delhi'),('Bengaluru'),('Hyderabad'),('Chennai'),('Pune'),
  ('Ahmedabad'),('Surat'),('Kolkata'),('Jaipur');

-- =============================================
-- INQUIRIES TABLE
-- =============================================
DROP TABLE IF EXISTS inquiries CASCADE;
CREATE TABLE inquiries (
    id                SERIAL PRIMARY KEY,
    hotel_name        VARCHAR(200) NOT NULL,
    client_name       VARCHAR(150) NOT NULL,
    client_number     VARCHAR(20)  NOT NULL,
    city_id           INT REFERENCES cfg_city(id) ON DELETE SET NULL,
    module_id         INT REFERENCES cfg_module(id) ON DELETE SET NULL,
    status_id         INT REFERENCES cfg_status(id) ON DELETE SET NULL,
    payment_received  BOOLEAN NOT NULL DEFAULT FALSE,
    followup_date     DATE,
    note              TEXT,
    is_converted      BOOLEAN NOT NULL DEFAULT FALSE,
    converted_client_id INT REFERENCES clients(id) ON DELETE SET NULL,  -- FK: prevents dangling reference
    is_deleted        BOOLEAN NOT NULL DEFAULT FALSE,
    created_by        INT REFERENCES users(id) ON DELETE SET NULL,
    updated_by        INT REFERENCES users(id) ON DELETE SET NULL,
    created_at        TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at        TIMESTAMP NOT NULL DEFAULT NOW()
);
-- Prevent duplicate inquiries by phone
CREATE UNIQUE INDEX idx_inquiries_phone_active
    ON inquiries(client_number) WHERE is_deleted = FALSE;

-- =============================================
-- CLIENTS TABLE
-- =============================================
DROP TABLE IF EXISTS clients CASCADE;
CREATE TABLE clients (
    id              SERIAL PRIMARY KEY,
    client_ref      VARCHAR(20)  NOT NULL UNIQUE,  -- AUTO: LMS-0001
    company_name    VARCHAR(200) NOT NULL,
    contact_person  VARCHAR(150) NOT NULL,
    phone           VARCHAR(20)  NOT NULL,
    email           VARCHAR(150),
    city_id         INT REFERENCES cfg_city(id) ON DELETE SET NULL,
    module_id       INT REFERENCES cfg_module(id) ON DELETE SET NULL,
    room_size       VARCHAR(50),
    address         TEXT,
    notes           TEXT,
    total_amount    NUMERIC(12,2) NOT NULL DEFAULT 0,
    source_inquiry_id INT REFERENCES inquiries(id) ON DELETE SET NULL,  -- FK: prevents dangling reference
    is_active       BOOLEAN  NOT NULL DEFAULT TRUE,
    is_deleted      BOOLEAN  NOT NULL DEFAULT FALSE,
    user_id         INT REFERENCES users(id) ON DELETE SET NULL,
    created_by      INT REFERENCES users(id) ON DELETE SET NULL,
    updated_by      INT REFERENCES users(id) ON DELETE SET NULL,
    created_at      TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMP NOT NULL DEFAULT NOW()
);
-- Prevent duplicate clients by phone
CREATE UNIQUE INDEX idx_clients_phone_active
    ON clients(phone) WHERE is_deleted = FALSE;

-- Prevent duplicate clients by email (when email is provided)
CREATE UNIQUE INDEX idx_clients_email_active
    ON clients(email) WHERE is_deleted = FALSE AND email IS NOT NULL;

-- Sequence for client_ref
CREATE SEQUENCE IF NOT EXISTS client_ref_seq START 1;

-- =============================================
-- PAYMENTS TABLE (Income)
-- =============================================
DROP TABLE IF EXISTS payments CASCADE;
CREATE TABLE payments (
    id              SERIAL PRIMARY KEY,
    client_id       INT NOT NULL REFERENCES clients(id) ON DELETE RESTRICT,
    amount          NUMERIC(12,2) NOT NULL,
    payment_mode    VARCHAR(20)  NOT NULL DEFAULT 'Cash',  -- Cash, Cheque, Online
    cheque_no       VARCHAR(50),
    bank_name       VARCHAR(100),
    transaction_id  VARCHAR(100),
    payment_date    DATE NOT NULL DEFAULT CURRENT_DATE,
    note            TEXT,
    proof_file      VARCHAR(500),   -- stored file path
    is_deleted      BOOLEAN NOT NULL DEFAULT FALSE,
    created_by      INT REFERENCES users(id) ON DELETE SET NULL,
    updated_by      INT REFERENCES users(id) ON DELETE SET NULL,
    created_at      TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMP NOT NULL DEFAULT NOW()
);

-- =============================================
-- EXPENSES TABLE
-- =============================================
DROP TABLE IF EXISTS expenses CASCADE;
CREATE TABLE expenses (
    id              SERIAL PRIMARY KEY,
    expense_date    DATE NOT NULL DEFAULT CURRENT_DATE,
    category_id     INT REFERENCES cfg_category(id) ON DELETE SET NULL,
    from_name       VARCHAR(150),
    to_name         VARCHAR(150),
    amount          NUMERIC(12,2) NOT NULL,
    payment_mode    VARCHAR(20)  NOT NULL DEFAULT 'Cash',
    cheque_no       VARCHAR(50),
    bank_name       VARCHAR(100),
    transaction_id  VARCHAR(100),
    note            TEXT,
    attachment      VARCHAR(500),
    is_deleted      BOOLEAN NOT NULL DEFAULT FALSE,
    created_by      INT REFERENCES users(id) ON DELETE SET NULL,
    updated_by      INT REFERENCES users(id) ON DELETE SET NULL,
    created_at      TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMP NOT NULL DEFAULT NOW()
);

-- NOTE: Generate a BCrypt hash for your admin password using:
--   dotnet run -- seed-admin   (or run the app once and register with Admin role)
-- Example (hash for 'Admin@1234' at work-factor 12 — generate a real one before go-live):
--   INSERT INTO users (full_name, email, password, role)
--     VALUES ('Super Admin','admin@hotel.com','$2a$12$<your_bcrypt_hash_here>','Admin');

-- =============================================
-- MIGRATION: apply to existing databases
-- =============================================
-- ALTER TABLE users ADD COLUMN IF NOT EXISTS is_deleted BOOLEAN NOT NULL DEFAULT FALSE;
-- ALTER TABLE users ALTER COLUMN role SET DEFAULT 'Client';
-- ALTER TABLE clients ADD CONSTRAINT clients_source_inquiry_id_fk
--   FOREIGN KEY (source_inquiry_id) REFERENCES inquiries(id) ON DELETE SET NULL;
-- ALTER TABLE inquiries ADD CONSTRAINT inquiries_converted_client_id_fk
--   FOREIGN KEY (converted_client_id) REFERENCES clients(id) ON DELETE SET NULL;
-- CREATE UNIQUE INDEX IF NOT EXISTS idx_clients_email_active
--   ON clients(email) WHERE is_deleted = FALSE AND email IS NOT NULL;