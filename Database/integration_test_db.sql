﻿-- Create the database
CREATE DATABASE funky_db;
GO

USE funky_db;
GO

-- Table: person
CREATE TABLE person (
    id INT IDENTITY(1,1) PRIMARY KEY,
    first_name NVARCHAR(100) NOT NULL,
    middle_initial CHAR(1) NULL,
    last_name NVARCHAR(100) NOT NULL,
    birthdate DATE NULL,
    gender NVARCHAR(10) NULL,
    uniqueid UNIQUEIDENTIFIER NULL,
    dateutc_created DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    dateutc_modified DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);

-- Table: address
CREATE TABLE address (
    id INT IDENTITY(1,1) PRIMARY KEY,
    line_1 NVARCHAR(255) NOT NULL,
    line_2 NVARCHAR(255) NULL,
    city NVARCHAR(100) NOT NULL,
    state_code CHAR(2) NOT NULL,
    postal_code NVARCHAR(20) NOT NULL,
    is_primary BIT NOT NULL DEFAULT 0,
    dateutc_created DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    dateutc_modified DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);

-- Table: person_address (for many-to-many relationship)
CREATE TABLE person_address (
    id INT IDENTITY(1,1) PRIMARY KEY,
    person_id INT NOT NULL,
    address_id INT NOT NULL,
    dateutc_created DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    dateutc_modified DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT FK_person_address_person FOREIGN KEY (person_id) REFERENCES person(id),
    CONSTRAINT FK_person_address_address FOREIGN KEY (address_id) REFERENCES address(id)
);

-- Create indexes for performance (optional but recommended)
CREATE INDEX IX_person_address_person ON person_address(person_id);
CREATE INDEX IX_person_address_address ON person_address(address_id);
GO

-- Trigger for updating dateutc_modified in person table
CREATE TRIGGER trg_person_update 
ON person
AFTER UPDATE 
AS 
BEGIN
    UPDATE p SET 
        dateutc_modified = GETUTCDATE()
    FROM person p 
    INNER JOIN inserted i ON p.id = i.id;
END;
GO

-- Trigger for updating dateutc_modified in address table
CREATE TRIGGER trg_address_update 
ON address
AFTER UPDATE 
AS 
BEGIN
    UPDATE a SET 
        dateutc_modified = GETUTCDATE()
    FROM address a 
    INNER JOIN inserted i ON a.id = i.id;
END;
GO

-- Trigger for updating dateutc_modified in person_address table
CREATE TRIGGER trg_person_address_update 
ON person_address
AFTER UPDATE 
AS 
BEGIN
    UPDATE pa SET 
        dateutc_modified = GETUTCDATE()
    FROM person_address pa 
    INNER JOIN inserted i ON pa.id = i.id;
END;