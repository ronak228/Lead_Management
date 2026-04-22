-- =============================================
-- LeadManagementSystem – PostgreSQL Schema v3 (IMPROVED)
-- Comprehensive fixes for all structural weaknesses
-- =============================================

-- =============================================
-- USERS TABLE (Admin, Employee, Client consolidated)
-- =============================================
DROP TABLE IF EXISTS users CASCADE;
CREATE TABLE users (
    id          SERIAL PRIMARY KEY,
    full_name   VARCHAR(150) NOT NULL,
    email       VARCHAR(150) NOT NULL UNIQUE,
    password    VARCHAR(256) NOT NULL,
    role        VARCHAR(20)  NOT NULL DEFAULT 'Client' CHECK (role IN ('Admin','Employee','Client')),
    is_active   BOOLEAN      NOT NULL DEFAULT TRUE,
    is_deleted  BOOLEAN      NOT NULL DEFAULT FALSE,
    -- Client-specific fields (NULL for Admin/Employee)
    client_ref  VARCHAR(20) UNIQUE,  -- unique for active clients only
    company_name    VARCHAR(200),
    contact_person  VARCHAR(150),
    phone           VARCHAR(20),
    city_id         INT REFERENCES cfg_city(id) ON DELETE SET NULL,
    module_id       INT REFERENCES cfg_module(id) ON DELETE SET NULL,
    room_size       VARCHAR(50),
    address         TEXT,
    notes           TEXT,
    total_amount    NUMERIC(12,2) NOT NULL DEFAULT 0 CHECK (total_amount >= 0),
    source_inquiry_id INT,  -- will add FK after inquiries table is created
    -- System fields
    setup_token        VARCHAR(100),
    setup_token_expires TIMESTAMP,
    password_reset_token VARCHAR(256),
    password_reset_expires TIMESTAMP,
    created_by      INT REFERENCES users(id) ON DELETE SET NULL,
    updated_by      INT REFERENCES users(id) ON DELETE SET NULL,
    created_at      TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMP NOT NULL DEFAULT NOW()
);

-- Indexes for users table
CREATE UNIQUE INDEX idx_users_email_active ON users(email) WHERE is_deleted = FALSE;
CREATE UNIQUE INDEX idx_users_client_ref_active ON users(client_ref) WHERE is_deleted = FALSE AND role='Client' AND client_ref IS NOT NULL;
CREATE UNIQUE INDEX idx_users_phone_client_active ON users(phone) WHERE is_deleted = FALSE AND role='Client' AND phone IS NOT NULL;
CREATE INDEX idx_users_role ON users(role);
CREATE INDEX idx_users_is_active ON users(is_active);
CREATE INDEX idx_users_is_deleted ON users(is_deleted);
CREATE INDEX idx_users_created_at ON users(created_at DESC);
CREATE INDEX idx_users_updated_at ON users(updated_at DESC);
CREATE SEQUENCE IF NOT EXISTS client_ref_seq START 1;

-- =============================================
-- CONFIGURATION TABLES
-- =============================================
DROP TABLE IF EXISTS cfg_status CASCADE;
CREATE TABLE cfg_status (
    id         SERIAL PRIMARY KEY,
    name       VARCHAR(100) NOT NULL UNIQUE,
    is_active  BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_cfg_status_active ON cfg_status(is_active);

DROP TABLE IF EXISTS cfg_module CASCADE;
CREATE TABLE cfg_module (
    id         SERIAL PRIMARY KEY,
    name       VARCHAR(100) NOT NULL UNIQUE,
    is_active  BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_cfg_module_active ON cfg_module(is_active);

DROP TABLE IF EXISTS cfg_product CASCADE;
CREATE TABLE cfg_product (
    id         SERIAL PRIMARY KEY,
    name       VARCHAR(100) NOT NULL UNIQUE,
    is_active  BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_cfg_product_active ON cfg_product(is_active);

DROP TABLE IF EXISTS cfg_category CASCADE;
CREATE TABLE cfg_category (
    id         SERIAL PRIMARY KEY,
    name       VARCHAR(100) NOT NULL UNIQUE,
    is_active  BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_cfg_category_active ON cfg_category(is_active);

DROP TABLE IF EXISTS cfg_city CASCADE;
CREATE TABLE cfg_city (
    id         SERIAL PRIMARY KEY,
    name       VARCHAR(100) NOT NULL UNIQUE,
    is_active  BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_cfg_city_active ON cfg_city(is_active);

-- =============================================
-- SEED DATA
-- =============================================
INSERT INTO cfg_status (name, is_active) VALUES
  ('Received', TRUE),
  ('In Progress', TRUE),
  ('Completed', TRUE),
  ('Negotiation', TRUE),
  ('Converted', TRUE),
  ('Lost', TRUE)
ON CONFLICT (name) DO NOTHING;

INSERT INTO cfg_module (name, is_active) VALUES
  ('Housekeeping', TRUE),
  ('Front Office', TRUE),
  ('Food & Beverage', TRUE),
  ('Banquet', TRUE),
  ('Maintenance', TRUE),
  ('Accounts', TRUE)
ON CONFLICT (name) DO NOTHING;

INSERT INTO cfg_product (name, is_active) VALUES
  ('Room Management', TRUE),
  ('POS System', TRUE),
  ('Booking Engine', TRUE),
  ('Payroll', TRUE),
  ('Inventory', TRUE),
  ('CRM', TRUE)
ON CONFLICT (name) DO NOTHING;

INSERT INTO cfg_category (name, is_active) VALUES
  ('Office Expense', TRUE),
  ('Travel', TRUE),
  ('Utilities', TRUE),
  ('Marketing', TRUE),
  ('Maintenance', TRUE),
  ('Salary', TRUE)
ON CONFLICT (name) DO NOTHING;

INSERT INTO cfg_city (name, is_active) VALUES
  ('Mumbai', TRUE),
  ('Delhi', TRUE),
  ('Bengaluru', TRUE),
  ('Hyderabad', TRUE),
  ('Chennai', TRUE),
  ('Pune', TRUE),
  ('Ahmedabad', TRUE),
  ('Surat', TRUE),
  ('Kolkata', TRUE),
  ('Jaipur', TRUE)
ON CONFLICT (name) DO NOTHING;

-- =============================================
-- INQUIRIES TABLE
-- =============================================
DROP TABLE IF EXISTS inquiries CASCADE;
CREATE TABLE inquiries (
    id                SERIAL PRIMARY KEY,
    hotel_name        VARCHAR(200) NOT NULL,
    client_name       VARCHAR(150) NOT NULL,
    client_number     VARCHAR(20)  NOT NULL,  -- phone number (NOT unique - multiple inquiries same phone)
    city_id           INT REFERENCES cfg_city(id) ON DELETE SET NULL,
    module_id         INT REFERENCES cfg_module(id) ON DELETE SET NULL,
    status_id         INT REFERENCES cfg_status(id) ON DELETE SET NULL,
    payment_received  BOOLEAN NOT NULL DEFAULT FALSE,
    followup_date     DATE,
    note              TEXT,
    is_converted      BOOLEAN NOT NULL DEFAULT FALSE,
    converted_client_id INT REFERENCES users(id) ON DELETE SET NULL,
    is_deleted        BOOLEAN NOT NULL DEFAULT FALSE,
    created_by        INT NOT NULL REFERENCES users(id) ON DELETE CASCADE,  -- inquiry creator (important!)
    updated_by        INT REFERENCES users(id) ON DELETE SET NULL,
    created_at        TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at        TIMESTAMP NOT NULL DEFAULT NOW()
);

-- REMOVED: UNIQUE constraint on client_number (multiple inquiries can have same phone!)
-- Indexes for inquiries
CREATE INDEX idx_inquiries_client_number ON inquiries(client_number) WHERE is_deleted = FALSE;
CREATE INDEX idx_inquiries_created_by ON inquiries(created_by);
CREATE INDEX idx_inquiries_converted_client_id ON inquiries(converted_client_id);
CREATE INDEX idx_inquiries_city_id ON inquiries(city_id);
CREATE INDEX idx_inquiries_module_id ON inquiries(module_id);
CREATE INDEX idx_inquiries_status_id ON inquiries(status_id);
CREATE INDEX idx_inquiries_followup_date ON inquiries(followup_date) WHERE followup_date IS NOT NULL;
CREATE INDEX idx_inquiries_is_converted ON inquiries(is_converted) WHERE is_converted = TRUE;
CREATE INDEX idx_inquiries_created_at ON inquiries(created_at DESC);
CREATE INDEX idx_inquiries_is_deleted ON inquiries(is_deleted);

-- =============================================
-- ADD FK CONSTRAINT for users.source_inquiry_id (NOW THAT inquiries EXISTS)
-- =============================================
ALTER TABLE users ADD CONSTRAINT fk_users_source_inquiry_id
    FOREIGN KEY (source_inquiry_id) REFERENCES inquiries(id) ON DELETE SET NULL;

-- =============================================
-- PAYMENTS TABLE (Income)
-- =============================================
DROP TABLE IF EXISTS payments CASCADE;
CREATE TABLE payments (
    id              SERIAL PRIMARY KEY,
    client_id       INT NOT NULL REFERENCES users(id) ON DELETE CASCADE,  -- soft-delete via is_deleted
    amount          NUMERIC(12,2) NOT NULL CHECK (amount > 0),  -- amounts must be positive
    payment_mode    VARCHAR(20)  NOT NULL DEFAULT 'Cash' CHECK (payment_mode IN ('Cash', 'Cheque', 'Online')),
    cheque_no       VARCHAR(50),
    bank_name       VARCHAR(100),
    transaction_id  VARCHAR(100),
    payment_date    DATE NOT NULL DEFAULT CURRENT_DATE,
    note            TEXT,
    proof_file      VARCHAR(500),
    is_deleted      BOOLEAN NOT NULL DEFAULT FALSE,
    created_by      INT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    updated_by      INT REFERENCES users(id) ON DELETE SET NULL,
    created_at      TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMP NOT NULL DEFAULT NOW()
);

-- Indexes for payments
CREATE INDEX idx_payments_client_id ON payments(client_id) WHERE is_deleted = FALSE;
CREATE INDEX idx_payments_payment_date ON payments(payment_date DESC) WHERE is_deleted = FALSE;
CREATE INDEX idx_payments_created_by ON payments(created_by);
CREATE INDEX idx_payments_created_at ON payments(created_at DESC);
CREATE INDEX idx_payments_is_deleted ON payments(is_deleted);

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
    amount          NUMERIC(12,2) NOT NULL CHECK (amount > 0),  -- amounts must be positive
    payment_mode    VARCHAR(20)  NOT NULL DEFAULT 'Cash' CHECK (payment_mode IN ('Cash', 'Cheque', 'Online')),
    cheque_no       VARCHAR(50),
    bank_name       VARCHAR(100),
    transaction_id  VARCHAR(100),
    note            TEXT,
    attachment      VARCHAR(500),
    is_deleted      BOOLEAN NOT NULL DEFAULT FALSE,
    created_by      INT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    updated_by      INT REFERENCES users(id) ON DELETE SET NULL,
    created_at      TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMP NOT NULL DEFAULT NOW()
);

-- Indexes for expenses
CREATE INDEX idx_expenses_expense_date ON expenses(expense_date DESC) WHERE is_deleted = FALSE;
CREATE INDEX idx_expenses_category_id ON expenses(category_id);
CREATE INDEX idx_expenses_created_by ON expenses(created_by);
CREATE INDEX idx_expenses_created_at ON expenses(created_at DESC);
CREATE INDEX idx_expenses_is_deleted ON expenses(is_deleted);

-- =============================================
-- AUDIT LOG TABLE (Compliance & Traceability)
-- =============================================
DROP TABLE IF EXISTS audit_log CASCADE;
CREATE TABLE audit_log (
    id              SERIAL PRIMARY KEY,
    entity_type     VARCHAR(50) NOT NULL,  -- 'User', 'Inquiry', 'Payment', 'Expense', etc.
    entity_id       INT NOT NULL,
    action          VARCHAR(20) NOT NULL CHECK (action IN ('CREATE', 'UPDATE', 'DELETE')),
    old_values      JSONB,  -- previous values for updates/deletes
    new_values      JSONB,  -- new values for creates/updates
    changed_by      INT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    changed_at      TIMESTAMP NOT NULL DEFAULT NOW(),
    ip_address      VARCHAR(45),
    user_agent      VARCHAR(500)
);

-- Indexes for audit_log
CREATE INDEX idx_audit_log_entity ON audit_log(entity_type, entity_id) WHERE action != 'DELETE';
CREATE INDEX idx_audit_log_changed_by ON audit_log(changed_by);
CREATE INDEX idx_audit_log_changed_at ON audit_log(changed_at DESC);
CREATE INDEX idx_audit_log_action ON audit_log(action);

-- =============================================
-- LOGIN HISTORY TABLE (Security & Debugging)
-- =============================================
DROP TABLE IF EXISTS login_history CASCADE;
CREATE TABLE login_history (
    id              SERIAL PRIMARY KEY,
    user_id         INT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    login_at        TIMESTAMP NOT NULL DEFAULT NOW(),
    logout_at       TIMESTAMP,
    ip_address      VARCHAR(45),
    user_agent      VARCHAR(500),
    status          VARCHAR(20) NOT NULL DEFAULT 'SUCCESS' CHECK (status IN ('SUCCESS', 'FAILED', 'SESSION_TIMEOUT')),
    failure_reason  VARCHAR(200)
);

-- Indexes for login_history
CREATE INDEX idx_login_history_user_id ON login_history(user_id);
CREATE INDEX idx_login_history_login_at ON login_history(login_at DESC);
CREATE INDEX idx_login_history_status ON login_history(status);

-- =============================================
-- UPDATED_AT TRIGGERS (Auto-update timestamp)
-- =============================================
CREATE OR REPLACE FUNCTION update_timestamp()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trigger_users_updated_at BEFORE UPDATE ON users
    FOR EACH ROW EXECUTE FUNCTION update_timestamp();

CREATE TRIGGER trigger_inquiries_updated_at BEFORE UPDATE ON inquiries
    FOR EACH ROW EXECUTE FUNCTION update_timestamp();

CREATE TRIGGER trigger_payments_updated_at BEFORE UPDATE ON payments
    FOR EACH ROW EXECUTE FUNCTION update_timestamp();

CREATE TRIGGER trigger_expenses_updated_at BEFORE UPDATE ON expenses
    FOR EACH ROW EXECUTE FUNCTION update_timestamp();

-- =============================================
-- RELATIONSHIP SUMMARY
-- =============================================
-- users.id ---> inquiries.created_by (who created the inquiry)
-- users.id ---> inquiries.converted_client_id (which client the inquiry converted to)
-- users.source_inquiry_id ---> inquiries.id (which inquiry became this client)
-- users.id ---> payments.client_id (payments belong to clients)
-- users.id ---> expenses.created_by (who recorded the expense)
-- inquiries.client_number ---> users.phone (denormalized match for inquiry->client)
-- payments.client_id ---> users.id (strict FK with CASCADE delete)
-- inquiries.created_by ---> users.id (strict FK with CASCADE delete)
