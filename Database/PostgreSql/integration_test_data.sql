-- PostgreSQL Seed Data for FunkyORM integration tests
-- Uses PL/pgSQL anonymous blocks for procedural generation
-- Mirrors the SQL Server seed script (5,000 persons, ~5,000 addresses, ~5,000 person_address links)

-- ========== PERSON INSERTS (5,000 rows) ==========
DO $$
DECLARE
    v_counter INTEGER := 0;
    v_gender VARCHAR(10);
    v_first_name VARCHAR(100);
    v_last_name VARCHAR(100);
    v_rand_first INTEGER;
    v_rand_last INTEGER;
    v_is_common BOOLEAN;
BEGIN
    WHILE v_counter < 5000 LOOP
        v_gender := CASE WHEN random() < 0.5 THEN 'Male' ELSE 'Female' END;
        v_is_common := random() < 0.6;
        v_rand_first := floor(random() * 50)::INTEGER;
        v_rand_last := floor(random() * 50)::INTEGER;

        v_first_name := CASE
            WHEN v_gender = 'Male' AND v_is_common THEN CASE v_rand_first
                WHEN 0 THEN 'James' WHEN 1 THEN 'Robert' WHEN 2 THEN 'John' WHEN 3 THEN 'Michael' WHEN 4 THEN 'David'
                WHEN 5 THEN 'William' WHEN 6 THEN 'Richard' WHEN 7 THEN 'Joseph' WHEN 8 THEN 'Thomas' WHEN 9 THEN 'Daniel'
                WHEN 10 THEN 'Charles' WHEN 11 THEN 'Christopher' WHEN 12 THEN 'Matthew' WHEN 13 THEN 'Anthony' WHEN 14 THEN 'Mark'
                WHEN 15 THEN 'Donald' WHEN 16 THEN 'Steven' WHEN 17 THEN 'Paul' WHEN 18 THEN 'Andrew' WHEN 19 THEN 'Joshua'
                WHEN 20 THEN 'Kenneth' WHEN 21 THEN 'Kevin' WHEN 22 THEN 'Brian' WHEN 23 THEN 'George' WHEN 24 THEN 'Timothy'
                WHEN 25 THEN 'Ronald' WHEN 26 THEN 'Jason' WHEN 27 THEN 'Edward' WHEN 28 THEN 'Jeffrey' WHEN 29 THEN 'Ryan'
                WHEN 30 THEN 'Jacob' WHEN 31 THEN 'Gary' WHEN 32 THEN 'Nicholas' WHEN 33 THEN 'Eric' WHEN 34 THEN 'Jonathan'
                WHEN 35 THEN 'Stephen' WHEN 36 THEN 'Larry' WHEN 37 THEN 'Justin' WHEN 38 THEN 'Scott' WHEN 39 THEN 'Brandon'
                WHEN 40 THEN 'Benjamin' WHEN 41 THEN 'Samuel' WHEN 42 THEN 'Gregory' WHEN 43 THEN 'Alexander' WHEN 44 THEN 'Frank'
                WHEN 45 THEN 'Patrick' WHEN 46 THEN 'Raymond' WHEN 47 THEN 'Jack' WHEN 48 THEN 'Dennis' ELSE 'Jerry'
            END
            WHEN v_gender = 'Male' AND NOT v_is_common THEN CASE v_rand_first
                WHEN 0 THEN 'Ledger' WHEN 1 THEN 'Azariah' WHEN 2 THEN 'Donovan' WHEN 3 THEN 'Moses' WHEN 4 THEN 'Kaizen'
                WHEN 5 THEN 'Elio' WHEN 6 THEN 'Leonidas' WHEN 7 THEN 'Lawrence' WHEN 8 THEN 'Tripp' WHEN 9 THEN 'Ariel'
                WHEN 10 THEN 'Alonzo' WHEN 11 THEN 'Kaison' WHEN 12 THEN 'Lian' WHEN 13 THEN 'Devin' WHEN 14 THEN 'Rio'
                WHEN 15 THEN 'Johnathan' WHEN 16 THEN 'Ayaan' WHEN 17 THEN 'Gunner' WHEN 18 THEN 'Jeffrey' WHEN 19 THEN 'Philip'
                WHEN 20 THEN 'Samson' WHEN 21 THEN 'Moises' WHEN 22 THEN 'Lucca' WHEN 23 THEN 'Musa' WHEN 24 THEN 'Camilo'
                WHEN 25 THEN 'Hamza' WHEN 26 THEN 'Ridge' WHEN 27 THEN 'Kolton' WHEN 28 THEN 'Morgan' WHEN 29 THEN 'Troy'
                WHEN 30 THEN 'Kylan' WHEN 31 THEN 'Amiri' WHEN 32 THEN 'Boone' WHEN 33 THEN 'Makai' WHEN 34 THEN 'Johan'
                WHEN 35 THEN 'Bruce' WHEN 36 THEN 'Dorian' WHEN 37 THEN 'Gregory' WHEN 38 THEN 'Pierce' WHEN 39 THEN 'Roy'
                WHEN 40 THEN 'Drew' WHEN 41 THEN 'Clay' WHEN 42 THEN 'Caiden' WHEN 43 THEN 'Enrique' WHEN 44 THEN 'Jamir'
                WHEN 45 THEN 'Leland' WHEN 46 THEN 'Mohamed' WHEN 47 THEN 'Alessandro' WHEN 48 THEN 'Deacon' ELSE 'Augustine'
            END
            WHEN v_gender = 'Female' AND v_is_common THEN CASE v_rand_first
                WHEN 0 THEN 'Mary' WHEN 1 THEN 'Patricia' WHEN 2 THEN 'Jennifer' WHEN 3 THEN 'Linda' WHEN 4 THEN 'Elizabeth'
                WHEN 5 THEN 'Barbara' WHEN 6 THEN 'Susan' WHEN 7 THEN 'Jessica' WHEN 8 THEN 'Sarah' WHEN 9 THEN 'Karen'
                WHEN 10 THEN 'Lisa' WHEN 11 THEN 'Nancy' WHEN 12 THEN 'Betty' WHEN 13 THEN 'Sandra' WHEN 14 THEN 'Margaret'
                WHEN 15 THEN 'Ashley' WHEN 16 THEN 'Kimberly' WHEN 17 THEN 'Emily' WHEN 18 THEN 'Donna' WHEN 19 THEN 'Michelle'
                WHEN 20 THEN 'Carol' WHEN 21 THEN 'Amanda' WHEN 22 THEN 'Melissa' WHEN 23 THEN 'Deborah' WHEN 24 THEN 'Stephanie'
                WHEN 25 THEN 'Dorothy' WHEN 26 THEN 'Rebecca' WHEN 27 THEN 'Sharon' WHEN 28 THEN 'Laura' WHEN 29 THEN 'Cynthia'
                WHEN 30 THEN 'Amy' WHEN 31 THEN 'Kathleen' WHEN 32 THEN 'Angela' WHEN 33 THEN 'Shirley' WHEN 34 THEN 'Brenda'
                WHEN 35 THEN 'Emma' WHEN 36 THEN 'Anna' WHEN 37 THEN 'Pamela' WHEN 38 THEN 'Nicole' WHEN 39 THEN 'Samantha'
                WHEN 40 THEN 'Katherine' WHEN 41 THEN 'Christine' WHEN 42 THEN 'Helen' WHEN 43 THEN 'Debra' WHEN 44 THEN 'Rachel'
                WHEN 45 THEN 'Carolyn' WHEN 46 THEN 'Janet' WHEN 47 THEN 'Maria' WHEN 48 THEN 'Catherine' ELSE 'Heather'
            END
            ELSE CASE v_rand_first
                WHEN 0 THEN 'April' WHEN 1 THEN 'Izabella' WHEN 2 THEN 'Hanna' WHEN 3 THEN 'Marceline' WHEN 4 THEN 'Alexis'
                WHEN 5 THEN 'Carter' WHEN 6 THEN 'Daniella' WHEN 7 THEN 'Marlee' WHEN 8 THEN 'Virginia' WHEN 9 THEN 'Kataleya'
                WHEN 10 THEN 'Halo' WHEN 11 THEN 'Nadia' WHEN 12 THEN 'Amiyah' WHEN 13 THEN 'Madelynn' WHEN 14 THEN 'Emerie'
                WHEN 15 THEN 'Renata' WHEN 16 THEN 'Oaklee' WHEN 17 THEN 'Remington' WHEN 18 THEN 'Maxine' WHEN 19 THEN 'Nellie'
                WHEN 20 THEN 'Briar' WHEN 21 THEN 'Danielle' WHEN 22 THEN 'Charli' WHEN 23 THEN 'Makenna' WHEN 24 THEN 'Imani'
                WHEN 25 THEN 'Armani' WHEN 26 THEN 'Edith' WHEN 27 THEN 'Nalani' WHEN 28 THEN 'Mae' WHEN 29 THEN 'Vienna'
                WHEN 30 THEN 'Hadassah' WHEN 31 THEN 'Stephanie' WHEN 32 THEN 'Ari' WHEN 33 THEN 'Kate' WHEN 34 THEN 'Jimena'
                WHEN 35 THEN 'Briana' WHEN 36 THEN 'Faye' WHEN 37 THEN 'Jordan' WHEN 38 THEN 'Louise' WHEN 39 THEN 'Amber'
                WHEN 40 THEN 'Makayla' WHEN 41 THEN 'Zahra' WHEN 42 THEN 'Lylah' WHEN 43 THEN 'Margo' WHEN 44 THEN 'Amoura'
                WHEN 45 THEN 'Jennifer' WHEN 46 THEN 'Kyla' WHEN 47 THEN 'Mylah' WHEN 48 THEN 'Winnie' ELSE 'Alisson'
            END
        END;

        v_last_name := CASE WHEN v_is_common THEN CASE v_rand_last
                WHEN 0 THEN 'Smith' WHEN 1 THEN 'Johnson' WHEN 2 THEN 'Williams' WHEN 3 THEN 'Brown' WHEN 4 THEN 'Jones'
                WHEN 5 THEN 'Garcia' WHEN 6 THEN 'Miller' WHEN 7 THEN 'Davis' WHEN 8 THEN 'Rodriguez' WHEN 9 THEN 'Martinez'
                WHEN 10 THEN 'Hernandez' WHEN 11 THEN 'Lopez' WHEN 12 THEN 'Gonzalez' WHEN 13 THEN 'Wilson' WHEN 14 THEN 'Anderson'
                WHEN 15 THEN 'Thomas' WHEN 16 THEN 'Taylor' WHEN 17 THEN 'Moore' WHEN 18 THEN 'Jackson' WHEN 19 THEN 'Martin'
                WHEN 20 THEN 'Lee' WHEN 21 THEN 'Perez' WHEN 22 THEN 'Thompson' WHEN 23 THEN 'White' WHEN 24 THEN 'Harris'
                WHEN 25 THEN 'Sanchez' WHEN 26 THEN 'Clark' WHEN 27 THEN 'Ramirez' WHEN 28 THEN 'Lewis' WHEN 29 THEN 'Robinson'
                WHEN 30 THEN 'Walker' WHEN 31 THEN 'Young' WHEN 32 THEN 'Allen' WHEN 33 THEN 'King' WHEN 34 THEN 'Wright'
                WHEN 35 THEN 'Scott' WHEN 36 THEN 'Torres' WHEN 37 THEN 'Nguyen' WHEN 38 THEN 'Hill' WHEN 39 THEN 'Flores'
                WHEN 40 THEN 'Green' WHEN 41 THEN 'Adams' WHEN 42 THEN 'Nelson' WHEN 43 THEN 'Baker' WHEN 44 THEN 'Hall'
                WHEN 45 THEN 'Rivera' WHEN 46 THEN 'Campbell' WHEN 47 THEN 'Mitchell' WHEN 48 THEN 'Carter' ELSE 'Roberts'
            END ELSE CASE v_rand_last
                WHEN 0 THEN 'Gomez' WHEN 1 THEN 'Phillips' WHEN 2 THEN 'Evans' WHEN 3 THEN 'Turner' WHEN 4 THEN 'Diaz'
                WHEN 5 THEN 'Parker' WHEN 6 THEN 'Cruz' WHEN 7 THEN 'Edwards' WHEN 8 THEN 'Collins' WHEN 9 THEN 'Reyes'
                WHEN 10 THEN 'Stewart' WHEN 11 THEN 'Morris' WHEN 12 THEN 'Morales' WHEN 13 THEN 'Murphy' WHEN 14 THEN 'Cook'
                WHEN 15 THEN 'Rogers' WHEN 16 THEN 'Gutierrez' WHEN 17 THEN 'Ortiz' WHEN 18 THEN 'Morgan' WHEN 19 THEN 'Cooper'
                WHEN 20 THEN 'Peterson' WHEN 21 THEN 'Bailey' WHEN 22 THEN 'Reed' WHEN 23 THEN 'Kelly' WHEN 24 THEN 'Howard'
                WHEN 25 THEN 'Ramos' WHEN 26 THEN 'Kim' WHEN 27 THEN 'Cox' WHEN 28 THEN 'Ward' WHEN 29 THEN 'Richardson'
                WHEN 30 THEN 'Watson' WHEN 31 THEN 'Brooks' WHEN 32 THEN 'Chavez' WHEN 33 THEN 'Wood' WHEN 34 THEN 'James'
                WHEN 35 THEN 'Bennett' WHEN 36 THEN 'Gray' WHEN 37 THEN 'Mendoza' WHEN 38 THEN 'Ruiz' WHEN 39 THEN 'Hughes'
                WHEN 40 THEN 'Price' WHEN 41 THEN 'Alvarez' WHEN 42 THEN 'Castillo' WHEN 43 THEN 'Sanders' WHEN 44 THEN 'Patel'
                WHEN 45 THEN 'Myers' WHEN 46 THEN 'Long' WHEN 47 THEN 'Ross' WHEN 48 THEN 'Foster' ELSE 'Jimenez'
            END
        END;

        INSERT INTO person (first_name, middle_initial, last_name, birthdate, gender, uniqueid)
        VALUES (
            v_first_name,
            CASE WHEN random() < 0.5 THEN chr(65 + floor(random() * 26)::INTEGER) ELSE NULL END,
            v_last_name,
            CURRENT_DATE - (floor(random() * 60 * 365)::INTEGER || ' days')::INTERVAL,
            v_gender,
            gen_random_uuid()
        );
        v_counter := v_counter + 1;
    END LOOP;
END $$;

-- ========== ADDRESS + PERSON_ADDRESS INSERTS ==========
-- Shuffled persons and assignments using temp tables

CREATE TEMP TABLE IF NOT EXISTS tmp_shuffled_persons AS
SELECT ROW_NUMBER() OVER (ORDER BY random()) AS rn, id AS person_id
FROM person;

CREATE TEMP TABLE IF NOT EXISTS tmp_assignments (person_id INTEGER, addr_num INTEGER, is_primary BOOLEAN);
INSERT INTO tmp_assignments (person_id, addr_num, is_primary)
SELECT person_id, 1, TRUE FROM tmp_shuffled_persons WHERE rn <= 3333;
INSERT INTO tmp_assignments (person_id, addr_num, is_primary)
SELECT person_id, 2, FALSE FROM tmp_shuffled_persons WHERE rn <= 1667;

CREATE TEMP TABLE IF NOT EXISTS tmp_inserted_addresses (address_id INTEGER, person_id INTEGER, is_primary BOOLEAN);

DO $$
DECLARE
    rec RECORD;
    v_addr_id INTEGER;
    v_rand_cs INTEGER;
    v_streets TEXT[] := ARRAY['Main St','Maple Ave','Oak Blvd','Pine Rd','Birch Lane',
                              'Cedar Ct','Elm St','Walnut Dr','Chestnut Ave','Sycamore Rd',
                              'Hickory Ln','Poplar St','Willow Ave','Ash Blvd','Magnolia Dr',
                              'Dogwood Ct','Cherry Ln','Linden St','Banyan Rd','Spruce Ave'];
    v_cities TEXT[] := ARRAY['New York','Los Angeles','Chicago','Houston','Phoenix',
                             'Seattle','Denver','Boston','San Francisco','Miami'];
    v_states TEXT[] := ARRAY['NY','CA','IL','TX','AZ','WA','CO','MA','CA','FL'];
BEGIN
    FOR rec IN SELECT * FROM tmp_assignments LOOP
        v_rand_cs := floor(random() * 10)::INTEGER;
        INSERT INTO address (line_1, line_2, city, state_code, postal_code)
        VALUES (
            (floor(random() * 9900) + 100)::TEXT || ' ' || v_streets[floor(random() * 20)::INTEGER + 1],
            CASE WHEN random() < 0.5 THEN 'Apt ' || (floor(random() * 100) + 1)::TEXT ELSE NULL END,
            v_cities[v_rand_cs + 1],
            v_states[v_rand_cs + 1],
            (10000 + floor(random() * 90000))::TEXT
        )
        RETURNING id INTO v_addr_id;

        INSERT INTO tmp_inserted_addresses (address_id, person_id, is_primary)
        VALUES (v_addr_id, rec.person_id, rec.is_primary);
    END LOOP;
END $$;

-- Insert person_address links
INSERT INTO person_address (person_id, address_id, is_primary, address_type_value)
SELECT
    person_id,
    address_id,
    is_primary,
    CASE WHEN is_primary THEN 4 ELSE 2 END
FROM tmp_inserted_addresses;

-- Cleanup temp tables
DROP TABLE IF EXISTS tmp_inserted_addresses;
DROP TABLE IF EXISTS tmp_assignments;
DROP TABLE IF EXISTS tmp_shuffled_persons;
