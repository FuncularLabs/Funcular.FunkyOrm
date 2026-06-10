-- MySQL DDL for FunkyORM integration tests (MySQL 8.0+, InnoDB, utf8mb4)
-- Usage: mysql -u root -p < integration_test_db.sql
CREATE DATABASE IF NOT EXISTS funky_db CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
USE funky_db;

CREATE TABLE IF NOT EXISTS country (
    id   INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(100) NOT NULL
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS address (
    id               INT AUTO_INCREMENT PRIMARY KEY,
    line_1           VARCHAR(200) NOT NULL,
    line_2           VARCHAR(200) NULL,
    city             VARCHAR(100) NOT NULL,
    state_code       VARCHAR(10)  NOT NULL,
    postal_code      VARCHAR(20)  NOT NULL,
    country_id       INT NULL,
    dateutc_created  DATETIME(6) NOT NULL DEFAULT (UTC_TIMESTAMP(6)),
    dateutc_modified DATETIME(6) NOT NULL DEFAULT (UTC_TIMESTAMP(6)),
    CONSTRAINT fk_address_country FOREIGN KEY (country_id) REFERENCES country(id)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS organization (
    id                       INT AUTO_INCREMENT PRIMARY KEY,
    name                     VARCHAR(100) NOT NULL,
    headquarters_address_id  INT NULL,
    row_version              TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    CONSTRAINT fk_org_address FOREIGN KEY (headquarters_address_id) REFERENCES address(id)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS person (
    id               INT AUTO_INCREMENT PRIMARY KEY,
    first_name       VARCHAR(100) NOT NULL,
    middle_initial   VARCHAR(5)   NULL,
    last_name        VARCHAR(100) NOT NULL,
    birthdate        DATE NULL,
    gender           VARCHAR(10)  NULL,
    uniqueid         CHAR(36)     NULL,
    employer_id      INT NULL,
    dateutc_created  DATETIME(6) NOT NULL DEFAULT (UTC_TIMESTAMP(6)),
    dateutc_modified DATETIME(6) NOT NULL DEFAULT (UTC_TIMESTAMP(6)),
    CONSTRAINT fk_person_org FOREIGN KEY (employer_id) REFERENCES organization(id)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS person_address (
    id                 INT AUTO_INCREMENT PRIMARY KEY,
    person_id          INT NOT NULL,
    address_id         INT NOT NULL,
    is_primary         TINYINT(1) NOT NULL DEFAULT 0,
    address_type_value INT NOT NULL DEFAULT 0,
    dateutc_created    DATETIME(6) NOT NULL DEFAULT (UTC_TIMESTAMP(6)),
    dateutc_modified   DATETIME(6) NOT NULL DEFAULT (UTC_TIMESTAMP(6)),
    CONSTRAINT fk_pa_person  FOREIGN KEY (person_id)  REFERENCES person(id),
    CONSTRAINT fk_pa_address FOREIGN KEY (address_id) REFERENCES address(id)
) ENGINE=InnoDB;
CREATE INDEX ix_person_address_person  ON person_address(person_id);
CREATE INDEX ix_person_address_address ON person_address(address_id);

-- JSON + computed-attribute test tables (parity with SqlServer/PostgreSql JsonPath tests)
CREATE TABLE IF NOT EXISTS project_category (
    id   INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    code VARCHAR(50)  NOT NULL
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS project (
    id               INT AUTO_INCREMENT PRIMARY KEY,
    name             VARCHAR(200) NOT NULL,
    organization_id  INT NOT NULL,
    lead_id          INT NULL,
    category_id      INT NULL,
    budget           DECIMAL(12,2) NULL,
    score            INT NULL,
    metadata         JSON NULL,
    dateutc_created  DATETIME(6) NOT NULL DEFAULT (UTC_TIMESTAMP(6)),
    dateutc_modified DATETIME(6) NOT NULL DEFAULT (UTC_TIMESTAMP(6))
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS project_milestone (
    id             INT AUTO_INCREMENT PRIMARY KEY,
    project_id     INT NOT NULL,
    title          VARCHAR(200) NOT NULL,
    status         VARCHAR(50)  NOT NULL DEFAULT 'pending',
    due_date       DATE NULL,
    completed_date DATE NULL
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS project_note (
    id              INT AUTO_INCREMENT PRIMARY KEY,
    project_id      INT NOT NULL,
    author_id       INT NULL,
    content         TEXT NOT NULL,
    category        VARCHAR(50) NOT NULL DEFAULT 'general',
    dateutc_created DATETIME(6) NOT NULL DEFAULT (UTC_TIMESTAMP(6))
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS non_identity_guid_entity (
    id   CHAR(36) PRIMARY KEY,
    name VARCHAR(100) NOT NULL
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS non_identity_string_entity (
    id   VARCHAR(50) PRIMARY KEY,
    name VARCHAR(100) NOT NULL
) ENGINE=InnoDB;

-- Reserved-word table (tests backtick quoting)
CREATE TABLE IF NOT EXISTS `user` (
    `key`    INT AUTO_INCREMENT PRIMARY KEY,
    `name`   VARCHAR(100) NOT NULL,
    `order`  INT NOT NULL,
    `select` INT NOT NULL DEFAULT 0
) ENGINE=InnoDB;
