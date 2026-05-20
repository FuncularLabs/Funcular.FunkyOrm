-- SQLite integration test schema
-- This script creates the same tables used by the MSSQL and PostgreSQL test suites.

CREATE TABLE IF NOT EXISTS person (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    first_name TEXT,
    middle_initial TEXT,
    last_name TEXT,
    birthdate TEXT,
    gender TEXT,
    dateutc_created TEXT NOT NULL DEFAULT (datetime('now')),
    dateutc_modified TEXT NOT NULL DEFAULT (datetime('now')),
    unique_id TEXT,
    employer_id INTEGER
);

CREATE TABLE IF NOT EXISTS address (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    line_1 TEXT,
    line_2 TEXT,
    city TEXT,
    state_code TEXT,
    postal_code TEXT,
    dateutc_created TEXT NOT NULL DEFAULT (datetime('now')),
    dateutc_modified TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS person_address (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    person_id INTEGER NOT NULL,
    address_id INTEGER NOT NULL,
    is_primary INTEGER NOT NULL DEFAULT 0,
    address_type_value INTEGER DEFAULT 0,
    dateutc_created TEXT NOT NULL DEFAULT (datetime('now')),
    dateutc_modified TEXT NOT NULL DEFAULT (datetime('now')),
    FOREIGN KEY (person_id) REFERENCES person(id),
    FOREIGN KEY (address_id) REFERENCES address(id)
);

CREATE TABLE IF NOT EXISTS non_identity_guid_entity (
    id TEXT PRIMARY KEY,
    name TEXT
);

CREATE TABLE IF NOT EXISTS non_identity_string_entity (
    id TEXT PRIMARY KEY,
    name TEXT
);

-- Reserved-word table for testing quoted identifier handling
CREATE TABLE IF NOT EXISTS "User" (
    "Key" INTEGER PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT,
    "Order" INTEGER NOT NULL DEFAULT 0,
    "Select" INTEGER NOT NULL DEFAULT 0
);
