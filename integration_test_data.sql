﻿USE funky_db;
GO

-- Insert into address table (some addresses will be shared)
DECLARE @AddressCounter INT = 0;
WHILE @AddressCounter < 100
BEGIN
    INSERT INTO address (line_1, line_2, city, state_code, postal_code)
    SELECT TOP 1 
        CASE 
            WHEN @AddressCounter % 5 = 0 THEN '123 Main St'
            WHEN @AddressCounter % 5 = 1 THEN '456 Maple Ave'
            WHEN @AddressCounter % 5 = 2 THEN '789 Oak Blvd'
            WHEN @AddressCounter % 5 = 3 THEN '101 Pine Rd'
            ELSE '202 Birch Lane'
        END,
        CASE WHEN @AddressCounter % 2 = 0 THEN 'Apt 10' ELSE NULL END,
        CASE 
            WHEN @AddressCounter % 5 = 0 THEN 'New York'
            WHEN @AddressCounter % 5 = 1 THEN 'Los Angeles'
            WHEN @AddressCounter % 5 = 2 THEN 'Chicago'
            WHEN @AddressCounter % 5 = 3 THEN 'Houston'
            ELSE 'Phoenix'
        END,
        CASE 
            WHEN @AddressCounter % 5 = 0 THEN 'NY'
            WHEN @AddressCounter % 5 = 1 THEN 'CA'
            WHEN @AddressCounter % 5 = 2 THEN 'IL'
            WHEN @AddressCounter % 5 = 3 THEN 'TX'
            ELSE 'AZ'
        END,
        CASE 
            WHEN @AddressCounter % 5 = 0 THEN '10001'
            WHEN @AddressCounter % 5 = 1 THEN '90001'
            WHEN @AddressCounter % 5 = 2 THEN '60601'
            WHEN @AddressCounter % 5 = 3 THEN '77001'
            ELSE '85001'
        END
    FROM sys.objects;
    SET @AddressCounter = @AddressCounter + 1;
END;

-- Insert into person table
DECLARE @PersonCounter INT = 0;
WHILE @PersonCounter < 100
BEGIN
    INSERT INTO person (first_name, middle_initial, last_name, birthdate, gender)
    SELECT TOP 1 
        CASE 
            WHEN @PersonCounter % 5 = 0 THEN 'John'
            WHEN @PersonCounter % 5 = 1 THEN 'Jane'
            WHEN @PersonCounter % 5 = 2 THEN 'Michael'
            WHEN @PersonCounter % 5 = 3 THEN 'Emily'
            ELSE 'David'
        END,
        CASE WHEN @PersonCounter % 2 = 0 THEN 'A' ELSE NULL END,
        CASE 
            WHEN @PersonCounter % 5 = 0 THEN 'Doe'
            WHEN @PersonCounter % 5 = 1 THEN 'Smith'
            WHEN @PersonCounter % 5 = 2 THEN 'Brown'
            WHEN @PersonCounter % 5 = 3 THEN 'Taylor'
            ELSE 'Wilson'
        END,
        DATEADD(YEAR, -ABS(CHECKSUM(NEWID()) % 60), GETDATE()), -- Random birthdate within 60 years
        CASE WHEN @PersonCounter % 2 = 0 THEN 'Male' ELSE 'Female' END
    FROM sys.objects;
    SET @PersonCounter = @PersonCounter + 1;
END;

-- Insert into person_address (allowing for multiple addresses per person and shared addresses)
DECLARE @LinkCounter INT = 0;
WHILE @LinkCounter < 150 -- More links to simulate multiple addresses
BEGIN
    INSERT INTO person_address (person_id, address_id)
    SELECT TOP 1 
        ABS(CHECKSUM(NEWID()) % 100) + 1, -- Random person_id between 1 and 100
        ABS(CHECKSUM(NEWID()) % 100) + 1  -- Random address_id between 1 and 100
    FROM sys.objects;
    SET @LinkCounter = @LinkCounter + 1;
END;