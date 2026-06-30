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

-- =========================================================================
-- Stored Procedure Test Objects (v3.7.0)
-- Cover every execution mode: result set, scalar, non-query, output
-- parameters, and both parameter styles. Idempotent via CREATE OR ALTER.
-- =========================================================================

CREATE OR ALTER PROCEDURE sp_get_persons_by_gender
    @gender NVARCHAR(10)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT id, first_name, last_name, gender, birthdate
    FROM person
    WHERE gender = @gender;
END;
GO

CREATE OR ALTER PROCEDURE sp_get_person_by_id
    @person_id INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT id, first_name, last_name, gender, birthdate
    FROM person
    WHERE id = @person_id;
END;
GO

CREATE OR ALTER PROCEDURE sp_count_persons_by_gender
    @gender NVARCHAR(10)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT COUNT(*) FROM person WHERE gender = @gender;
END;
GO

CREATE OR ALTER PROCEDURE sp_insert_organization
    @name NVARCHAR(100),
    @headquarters_address_id INT = NULL
AS
BEGIN
    INSERT INTO organization (name, headquarters_address_id)
    VALUES (@name, @headquarters_address_id);
END;
GO

CREATE OR ALTER PROCEDURE sp_update_person_gender
    @person_id INT,
    @new_gender NVARCHAR(10)
AS
BEGIN
    UPDATE person SET gender = @new_gender WHERE id = @person_id;
END;
GO

CREATE OR ALTER PROCEDURE sp_get_persons_paged
    @page INT,
    @page_size INT,
    @total_count INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT @total_count = COUNT(*) FROM person;
    SELECT id, first_name, last_name, gender, birthdate
    FROM person
    ORDER BY id
    OFFSET (@page - 1) * @page_size ROWS
    FETCH NEXT @page_size ROWS ONLY;
END;
GO

CREATE OR ALTER PROCEDURE sp_search_persons
    @first_name NVARCHAR(100) = NULL,
    @last_name NVARCHAR(100) = NULL,
    @gender NVARCHAR(10) = NULL,
    @min_birthdate DATE = NULL,
    @max_birthdate DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT id, first_name, last_name, gender, birthdate
    FROM person
    WHERE (@first_name IS NULL OR first_name LIKE '%' + @first_name + '%')
      AND (@last_name IS NULL OR last_name LIKE '%' + @last_name + '%')
      AND (@gender IS NULL OR gender = @gender)
      AND (@min_birthdate IS NULL OR birthdate >= @min_birthdate)
      AND (@max_birthdate IS NULL OR birthdate <= @max_birthdate);
END;
GO

CREATE OR ALTER PROCEDURE sp_get_person_full_name
    @person_id INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT CONCAT(first_name, ' ', last_name) FROM person WHERE id = @person_id;
END;
GO

CREATE OR ALTER PROCEDURE sp_get_projects_by_org
    @organization_id INT,
    @min_budget DECIMAL(12,2) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT id, name, organization_id, lead_id, category_id, budget, score, metadata
    FROM project
    WHERE organization_id = @organization_id
      AND (@min_budget IS NULL OR budget >= @min_budget);
END;
GO

CREATE OR ALTER PROCEDURE sp_noop
AS
BEGIN
    SET NOCOUNT ON;
    -- intentionally empty
END;
GO


-- =========================================================================
-- Row-Level Security demo objects (v3.8.0) — audit/session-context tests.
-- Standalone table so the policy cannot affect any other test table.
-- =========================================================================
DROP SECURITY POLICY IF EXISTS dbo.rls_demo_policy;
GO
DROP FUNCTION IF EXISTS dbo.fn_rls_demo;
GO
DROP TABLE IF EXISTS rls_demo;
GO
CREATE TABLE rls_demo (
    id INT IDENTITY(1,1) PRIMARY KEY,
    owner_id NVARCHAR(64) NOT NULL,
    payload NVARCHAR(200) NULL
);
GO
CREATE FUNCTION dbo.fn_rls_demo(@owner_id NVARCHAR(64))
RETURNS TABLE WITH SCHEMABINDING AS
RETURN
    SELECT 1 AS allowed
    WHERE @owner_id = CONVERT(NVARCHAR(64), SESSION_CONTEXT(N'myapp.user_id'))
       OR EXISTS (
            SELECT 1
            FROM STRING_SPLIT(CONVERT(NVARCHAR(MAX), SESSION_CONTEXT(N'myapp.group_ids')), ',') AS s
            WHERE s.value = @owner_id
       );
GO
CREATE SECURITY POLICY dbo.rls_demo_policy
    ADD FILTER PREDICATE dbo.fn_rls_demo(owner_id) ON dbo.rls_demo,
    ADD BLOCK PREDICATE dbo.fn_rls_demo(owner_id) ON dbo.rls_demo
    WITH (STATE = ON);
GO
