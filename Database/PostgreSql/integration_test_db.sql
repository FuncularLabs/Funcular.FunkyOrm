-- PostgreSQL DDL for FunkyORM integration tests
-- Target: PostgreSQL 14+
-- Usage: Connect to funky_db, then run this script:
--   PGPASSWORD=<pwd> psql -h localhost -U <user> -d funky_db -f integration_test_db.sql

-- Table: country
CREATE TABLE IF NOT EXISTS country (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL
);

-- Table: address
CREATE TABLE IF NOT EXISTS address (
    id SERIAL PRIMARY KEY,
    line_1 VARCHAR(255) NOT NULL,
    line_2 VARCHAR(255),
    city VARCHAR(100) NOT NULL,
    state_code CHAR(2) NOT NULL,
    postal_code VARCHAR(20) NOT NULL,
    country_id INTEGER,
    dateutc_created TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
    dateutc_modified TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
    CONSTRAINT fk_address_country FOREIGN KEY (country_id) REFERENCES country(id)
);

-- Table: organization
CREATE TABLE IF NOT EXISTS organization (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    headquarters_address_id INTEGER,
    CONSTRAINT fk_organization_address FOREIGN KEY (headquarters_address_id)
        REFERENCES address(id)
);

-- Table: person
CREATE TABLE IF NOT EXISTS person (
    id SERIAL PRIMARY KEY,
    first_name VARCHAR(100) NOT NULL,
    middle_initial CHAR(1),
    last_name VARCHAR(100) NOT NULL,
    birthdate DATE,
    gender VARCHAR(10),
    uniqueid UUID,
    employer_id INTEGER,
    dateutc_created TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
    dateutc_modified TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
    CONSTRAINT fk_person_organization FOREIGN KEY (employer_id)
        REFERENCES organization(id)
);

-- Table: person_address (many-to-many link table)
CREATE TABLE IF NOT EXISTS person_address (
    id SERIAL PRIMARY KEY,
    person_id INTEGER NOT NULL,
    address_id INTEGER NOT NULL,
    is_primary BOOLEAN NOT NULL DEFAULT FALSE,
    address_type_value INTEGER NOT NULL DEFAULT 0,
    dateutc_created TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
    dateutc_modified TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
    CONSTRAINT fk_person_address_person FOREIGN KEY (person_id) REFERENCES person(id),
    CONSTRAINT fk_person_address_address FOREIGN KEY (address_id) REFERENCES address(id)
);

-- Indexes for join performance
CREATE INDEX IF NOT EXISTS ix_person_address_person ON person_address(person_id);
CREATE INDEX IF NOT EXISTS ix_person_address_address ON person_address(address_id);

-- Table: non_identity_guid_entity (GUID primary key, no auto-increment)
CREATE TABLE IF NOT EXISTS non_identity_guid_entity (
    id UUID PRIMARY KEY,
    name VARCHAR(100) NOT NULL
);

-- Table: non_identity_string_entity (string primary key, no auto-increment)
CREATE TABLE IF NOT EXISTS non_identity_string_entity (
    id VARCHAR(50) PRIMARY KEY,
    name VARCHAR(100) NOT NULL
);

-- Table: "User" (Reserved Word Test - tests identifier quoting)
-- Only reserved words (Key, Order, Select) are quoted; Name is not a reserved word
-- and must be unquoted so PostgreSQL stores it lowercase (matching ORM convention).
CREATE TABLE IF NOT EXISTS "User" (
    "Key" SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    "Order" INTEGER NOT NULL,
    "Select" BOOLEAN NOT NULL DEFAULT FALSE
);

-- Shared trigger function for updating dateutc_modified on any table
CREATE OR REPLACE FUNCTION update_dateutc_modified()
RETURNS TRIGGER AS $$
BEGIN
    NEW.dateutc_modified = NOW() AT TIME ZONE 'UTC';
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Trigger: person
DROP TRIGGER IF EXISTS trg_person_update ON person;
CREATE TRIGGER trg_person_update
    BEFORE UPDATE ON person
    FOR EACH ROW
    EXECUTE FUNCTION update_dateutc_modified();

-- Trigger: address
DROP TRIGGER IF EXISTS trg_address_update ON address;
CREATE TRIGGER trg_address_update
    BEFORE UPDATE ON address
    FOR EACH ROW
    EXECUTE FUNCTION update_dateutc_modified();

-- Trigger: person_address
DROP TRIGGER IF EXISTS trg_person_address_update ON person_address;
CREATE TRIGGER trg_person_address_update
    BEFORE UPDATE ON person_address
    FOR EACH ROW
    EXECUTE FUNCTION update_dateutc_modified();

-- =========================================================================
-- Stored Procedure Test Objects (v3.7.0) — CALL-based (non-query + scalar/INOUT)
-- =========================================================================

CREATE OR REPLACE PROCEDURE sp_update_person_gender(p_person_id INT, p_new_gender VARCHAR)
LANGUAGE plpgsql AS $$
BEGIN
    UPDATE person SET gender = p_new_gender WHERE id = p_person_id;
END;
$$;

CREATE OR REPLACE PROCEDURE sp_insert_organization(p_name VARCHAR, p_headquarters_address_id INT DEFAULT NULL)
LANGUAGE plpgsql AS $$
BEGIN
    INSERT INTO organization (name, headquarters_address_id) VALUES (p_name, p_headquarters_address_id);
END;
$$;

CREATE OR REPLACE PROCEDURE sp_count_persons_by_gender(IN p_gender VARCHAR, INOUT p_total INT DEFAULT 0)
LANGUAGE plpgsql AS $$
BEGIN
    SELECT COUNT(*) INTO p_total FROM person WHERE gender = p_gender;
END;
$$;

CREATE OR REPLACE PROCEDURE sp_noop()
LANGUAGE plpgsql AS $$
BEGIN
    -- intentionally empty
END;
$$;

-- =========================================================================
-- Row-Level Security demo objects (v3.8.0) — audit/session-context tests.
-- PostgreSQL superusers BYPASS RLS, so enforcement is validated via a dedicated
-- non-superuser login role (funky_rls_tester). Settings are namespaced under
-- "funky." (FunkyORM prefixes keys lacking a dot).
-- =========================================================================
DROP TABLE IF EXISTS rls_demo;
CREATE TABLE rls_demo (
    id SERIAL PRIMARY KEY,
    owner_id VARCHAR(64) NOT NULL,
    payload VARCHAR(200)
);
ALTER TABLE rls_demo ENABLE ROW LEVEL SECURITY;
ALTER TABLE rls_demo FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS rls_demo_policy ON rls_demo;
CREATE POLICY rls_demo_policy ON rls_demo
    USING (owner_id = current_setting('funky.UserId', true)
           OR owner_id = ANY (string_to_array(coalesce(current_setting('funky.TeamIds', true), ''), ',')))
    WITH CHECK (owner_id = current_setting('funky.UserId', true)
           OR owner_id = ANY (string_to_array(coalesce(current_setting('funky.TeamIds', true), ''), ',')));

DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'funky_rls_tester') THEN
    CREATE ROLE funky_rls_tester LOGIN PASSWORD 'funky_rls_pw';
  END IF;
END $$;

GRANT USAGE ON SCHEMA public TO funky_rls_tester;
GRANT SELECT, INSERT, UPDATE, DELETE ON rls_demo TO funky_rls_tester;
GRANT USAGE, SELECT ON SEQUENCE rls_demo_id_seq TO funky_rls_tester;
