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
