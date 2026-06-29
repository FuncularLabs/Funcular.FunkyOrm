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

-- =========================================================================
-- Stored Procedure Test Objects (v3.7.0)
-- DELIMITER blocks are required when this file is piped through the mysql CLI
-- (as the CI workflow does). DELIMITER is a client directive: when creating
-- these through MySqlConnector, send each CREATE PROCEDURE as its own command.
-- Parameters use a p_ prefix to avoid clashing with column names in the body.
-- =========================================================================

DROP PROCEDURE IF EXISTS sp_get_persons_by_gender;
DELIMITER $$
CREATE PROCEDURE sp_get_persons_by_gender(IN p_gender VARCHAR(10))
BEGIN
    SELECT id, first_name, last_name, gender, birthdate FROM person WHERE gender = p_gender;
END$$
DELIMITER ;

DROP PROCEDURE IF EXISTS sp_get_person_by_id;
DELIMITER $$
CREATE PROCEDURE sp_get_person_by_id(IN p_person_id INT)
BEGIN
    SELECT id, first_name, last_name, gender, birthdate FROM person WHERE id = p_person_id;
END$$
DELIMITER ;

DROP PROCEDURE IF EXISTS sp_get_all_persons;
DELIMITER $$
CREATE PROCEDURE sp_get_all_persons()
BEGIN
    SELECT id, first_name, last_name, gender, birthdate FROM person;
END$$
DELIMITER ;

DROP PROCEDURE IF EXISTS sp_count_persons_by_gender;
DELIMITER $$
CREATE PROCEDURE sp_count_persons_by_gender(IN p_gender VARCHAR(10))
BEGIN
    SELECT COUNT(*) FROM person WHERE gender = p_gender;
END$$
DELIMITER ;

DROP PROCEDURE IF EXISTS sp_get_person_full_name;
DELIMITER $$
CREATE PROCEDURE sp_get_person_full_name(IN p_person_id INT)
BEGIN
    SELECT CONCAT(first_name, ' ', last_name) FROM person WHERE id = p_person_id;
END$$
DELIMITER ;

DROP PROCEDURE IF EXISTS sp_insert_organization;
DELIMITER $$
CREATE PROCEDURE sp_insert_organization(IN p_name VARCHAR(100), IN p_headquarters_address_id INT)
BEGIN
    INSERT INTO organization (name, headquarters_address_id) VALUES (p_name, p_headquarters_address_id);
END$$
DELIMITER ;

DROP PROCEDURE IF EXISTS sp_update_person_gender;
DELIMITER $$
CREATE PROCEDURE sp_update_person_gender(IN p_person_id INT, IN p_new_gender VARCHAR(10))
BEGIN
    UPDATE person SET gender = p_new_gender WHERE id = p_person_id;
END$$
DELIMITER ;

DROP PROCEDURE IF EXISTS sp_get_persons_paged;
DELIMITER $$
CREATE PROCEDURE sp_get_persons_paged(IN p_page INT, IN p_page_size INT, OUT p_total_count INT)
BEGIN
    DECLARE v_offset INT;
    SET v_offset = (p_page - 1) * p_page_size;
    SELECT COUNT(*) INTO p_total_count FROM person;
    SELECT id, first_name, last_name, gender, birthdate FROM person
    ORDER BY id LIMIT p_page_size OFFSET v_offset;
END$$
DELIMITER ;

DROP PROCEDURE IF EXISTS sp_search_persons;
DELIMITER $$
CREATE PROCEDURE sp_search_persons(
    IN p_first_name VARCHAR(100),
    IN p_last_name VARCHAR(100),
    IN p_gender VARCHAR(10),
    IN p_min_birthdate DATE,
    IN p_max_birthdate DATE)
BEGIN
    SELECT id, first_name, last_name, gender, birthdate FROM person
    WHERE (p_first_name IS NULL OR first_name LIKE CONCAT('%', p_first_name, '%'))
      AND (p_last_name IS NULL OR last_name LIKE CONCAT('%', p_last_name, '%'))
      AND (p_gender IS NULL OR gender = p_gender)
      AND (p_min_birthdate IS NULL OR birthdate >= p_min_birthdate)
      AND (p_max_birthdate IS NULL OR birthdate <= p_max_birthdate);
END$$
DELIMITER ;

DROP PROCEDURE IF EXISTS sp_noop;
DELIMITER $$
CREATE PROCEDURE sp_noop()
BEGIN
    SET @dummy = 0;
END$$
DELIMITER ;

-- Audit/session-context probe (v3.8.0): returns the primed session user variables.
DROP PROCEDURE IF EXISTS sp_funky_session;
DELIMITER $$
CREATE PROCEDURE sp_funky_session()
BEGIN
    SELECT @UserId AS user_id, @TeamIds AS team_ids;
END$$
DELIMITER ;
