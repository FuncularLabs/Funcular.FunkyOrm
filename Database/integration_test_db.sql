-- Create the database
CREATE DATABASE funky_db;
GO

USE funky_db;
GO

-- Table: country
CREATE TABLE country (
    id INT IDENTITY(1,1) PRIMARY KEY,
    name NVARCHAR(100) NOT NULL
);

-- Table: address
CREATE TABLE address (
    id INT IDENTITY(1,1) PRIMARY KEY,
    line_1 NVARCHAR(255) NOT NULL,
    line_2 NVARCHAR(255) NULL,
    city NVARCHAR(100) NOT NULL,
    state_code CHAR(2) NOT NULL,
    postal_code NVARCHAR(20) NOT NULL,
    country_id INT NULL,
    dateutc_created DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    dateutc_modified DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT FK_address_country FOREIGN KEY (country_id) REFERENCES country(id)
);

-- Table: organization
CREATE TABLE organization (
    id INT IDENTITY(1,1) PRIMARY KEY,
    name NVARCHAR(100) NOT NULL,
    headquarters_address_id INT NULL,
    row_version ROWVERSION NOT NULL,
    CONSTRAINT FK_organization_address FOREIGN KEY (headquarters_address_id) REFERENCES address(id)
);

-- Table: person
CREATE TABLE person (
    id INT IDENTITY(1,1) PRIMARY KEY,
    first_name NVARCHAR(100) NOT NULL,
    middle_initial CHAR(1) NULL,
    last_name NVARCHAR(100) NOT NULL,
    birthdate DATE NULL,
    gender NVARCHAR(10) NULL,
    uniqueid UNIQUEIDENTIFIER NULL,
    employer_id INT NULL,
    dateutc_created DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    dateutc_modified DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT FK_person_organization FOREIGN KEY (employer_id) REFERENCES organization(id)
);

-- Table: person_address (for many-to-many relationship)
CREATE TABLE person_address (
    id INT IDENTITY(1,1) PRIMARY KEY,
    person_id INT NOT NULL,
    address_id INT NOT NULL,
    is_primary BIT NOT NULL DEFAULT 0,
    address_type_value INT NOT NULL DEFAULT 0,
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
GO

-- =========================================================================
-- JSON Attribute Test Tables
-- These tables model organization projects with rich metadata stored as
-- JSON, plus child records suitable for subquery-aggregate and
-- JSON-collection attribute demonstrations.
-- =========================================================================

-- Table: project_category (lookup for project types)
CREATE TABLE project_category (
    id INT IDENTITY(1,1) PRIMARY KEY,
    name NVARCHAR(100) NOT NULL,
    code NVARCHAR(50) NOT NULL
);
GO

-- Table: project (core table — contains a JSON metadata column)
CREATE TABLE project (
    id INT IDENTITY(1,1) PRIMARY KEY,
    name NVARCHAR(200) NOT NULL,
    organization_id INT NOT NULL,
    lead_id INT NULL,
    category_id INT NULL,
    budget DECIMAL(12,2) NULL,
    score INT NULL,
    -- JSON column holding flexible/semi-structured data:
    --   { "priority": "high", "tags": ["api","backend"],
    --     "client": { "name": "Acme Corp", "region": "NA" },
    --     "risk_level": 3 }
    metadata NVARCHAR(MAX) NULL,
    dateutc_created DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    dateutc_modified DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT FK_project_organization FOREIGN KEY (organization_id) REFERENCES organization(id),
    CONSTRAINT FK_project_person FOREIGN KEY (lead_id) REFERENCES person(id),
    CONSTRAINT FK_project_category FOREIGN KEY (category_id) REFERENCES project_category(id)
);
GO

-- Table: project_milestone (child records — subquery aggregates & JSON collections)
CREATE TABLE project_milestone (
    id INT IDENTITY(1,1) PRIMARY KEY,
    project_id INT NOT NULL,
    title NVARCHAR(200) NOT NULL,
    status NVARCHAR(50) NOT NULL DEFAULT 'pending',  -- pending | in_progress | completed | overdue
    due_date DATE NULL,
    completed_date DATE NULL,
    CONSTRAINT FK_milestone_project FOREIGN KEY (project_id) REFERENCES project(id)
);
GO

-- Table: project_note (child records — JSON collection demo)
CREATE TABLE project_note (
    id INT IDENTITY(1,1) PRIMARY KEY,
    project_id INT NOT NULL,
    author_id INT NULL,
    content NVARCHAR(MAX) NOT NULL,
    category NVARCHAR(50) NOT NULL DEFAULT 'general',  -- general | risk | decision | blocker
    dateutc_created DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT FK_note_project FOREIGN KEY (project_id) REFERENCES project(id),
    CONSTRAINT FK_note_person FOREIGN KEY (author_id) REFERENCES person(id)
);
GO

CREATE INDEX IX_project_organization ON project(organization_id);
CREATE INDEX IX_project_lead ON project(lead_id);
CREATE INDEX IX_project_category ON project(category_id);
CREATE INDEX IX_milestone_project ON project_milestone(project_id);
CREATE INDEX IX_note_project ON project_note(project_id);
GO

-- =========================================================================
-- Demonstration View: vw_project_scorecard
-- Exercises every capability category planned for JSON attributes:
--   A. Scalar JOIN              — [RemoteProperty]  (already in ORM)
--   B. Expression JOIN          — [SqlExpression]    (Phase 2)
--   E. Coalesce / Fallback      — [SqlExpression]    (Phase 2)
--   D. Subquery Aggregate       — [SubqueryAggregate](Phase 3)
--   C. JSON Projection          — [JsonCollection]   (Phase 4)
--   +  JSON Scalar Extraction   — [JsonPath]         (Phase 1)
-- =========================================================================
CREATE VIEW vw_project_scorecard AS
SELECT
    p.id,
    p.name,
    p.budget,
    p.organization_id,

    -- (A) Scalar JOIN — already handled by [RemoteProperty]
    o.name                              AS organization_name,

    -- (A) Scalar JOIN — lead person
    p.lead_id,

    -- (B) Expression JOIN — CONCAT across joined person columns
    CONCAT(lead.first_name,
           CASE WHEN lead.last_name IS NOT NULL
                THEN ' ' + lead.last_name ELSE '' END)
                                        AS lead_name,

    -- (A) Scalar JOIN — category lookup
    p.category_id,
    pc.name                             AS category_name,

    -- (Phase 1) JSON scalar extraction — JSON_VALUE on the same table
    JSON_VALUE(p.metadata, '$.priority')            AS priority,
    JSON_VALUE(p.metadata, '$.client.name')         AS client_name,
    JSON_VALUE(p.metadata, '$.client.region')       AS client_region,
    CAST(JSON_VALUE(p.metadata, '$.risk_level') AS INT) AS risk_level,

    -- (E) Coalesce / Fallback — computed value with column fallback
    COALESCE(ms_stats.computed_score, p.score)      AS effective_score,

    -- (D) Subquery Aggregate — total milestone count
    COALESCE(ms_stats.milestone_count, 0)           AS milestone_count,

    -- (D) Subquery Aggregate — conditional counts by status
    COALESCE(ms_stats.milestones_completed, 0)      AS milestones_completed,
    COALESCE(ms_stats.milestones_overdue, 0)        AS milestones_overdue,
    COALESCE(ms_stats.milestones_pending, 0)        AS milestones_pending,

    -- (D) Subquery Aggregate — note counts by category
    COALESCE(note_stats.note_count, 0)              AS note_count,
    COALESCE(note_stats.risk_note_count, 0)         AS risk_note_count,
    COALESCE(note_stats.blocker_note_count, 0)      AS blocker_note_count,

    -- (C) JSON Projection — milestones as JSON array
    (
        SELECT
            ms.title,
            ms.status,
            ms.due_date,
            ms.completed_date
        FROM project_milestone ms
        WHERE ms.project_id = p.id
        ORDER BY ms.due_date
        FOR JSON PATH
    )                                   AS milestones_json,

    -- (C) JSON Projection — notes as JSON array
    (
        SELECT
            pn.content,
            pn.category,
            CONCAT(author.first_name,
                   CASE WHEN author.last_name IS NOT NULL
                        THEN ' ' + author.last_name ELSE '' END)
                                        AS author_name,
            pn.dateutc_created          AS created
        FROM project_note pn
        LEFT JOIN person author ON pn.author_id = author.id
        WHERE pn.project_id = p.id
        ORDER BY pn.dateutc_created
        FOR JSON PATH
    )                                   AS notes_json

FROM
    project p
    LEFT JOIN organization o        ON p.organization_id = o.id
    LEFT JOIN person lead           ON p.lead_id = lead.id
    LEFT JOIN project_category pc   ON p.category_id = pc.id
    OUTER APPLY (
        SELECT
            COUNT(*)                                                        AS milestone_count,
            SUM(CASE WHEN ms.status = 'completed'   THEN 1 ELSE 0 END)     AS milestones_completed,
            SUM(CASE WHEN ms.status = 'overdue'      THEN 1 ELSE 0 END)    AS milestones_overdue,
            SUM(CASE WHEN ms.status = 'pending'      THEN 1 ELSE 0 END)    AS milestones_pending,
            CAST(ROUND(
                SUM(CASE WHEN ms.status = 'completed' THEN 1.0 ELSE 0 END)
                / NULLIF(COUNT(*), 0) * 100, 0
            ) AS INT) AS computed_score
        FROM project_milestone ms
        WHERE ms.project_id = p.id
    ) ms_stats
    OUTER APPLY (
        SELECT
            COUNT(*)                                                        AS note_count,
            SUM(CASE WHEN pn.category = 'risk'    THEN 1 ELSE 0 END)       AS risk_note_count,
            SUM(CASE WHEN pn.category = 'blocker'  THEN 1 ELSE 0 END)      AS blocker_note_count
        FROM project_note pn
        WHERE pn.project_id = p.id
    ) note_stats;
GO

-- Table: User (Reserved Word Test)
CREATE TABLE [User] (
    [Key] INT IDENTITY(1,1) PRIMARY KEY,
    [Name] NVARCHAR(100) NOT NULL,
    [Order] INT NOT NULL,
    [Select] BIT NOT NULL DEFAULT 0
);
GO

