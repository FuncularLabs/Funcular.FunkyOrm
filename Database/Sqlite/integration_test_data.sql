-- SQLite integration test seed data

INSERT INTO person (first_name, middle_initial, last_name, birthdate, gender, dateutc_created, dateutc_modified, unique_id)
VALUES
('John', 'A', 'Doe', '1990-01-15 00:00:00.000', 'Male', datetime('now'), datetime('now'), lower(hex(randomblob(16)))),
('Jane', 'B', 'Smith', '1985-06-20 00:00:00.000', 'Female', datetime('now'), datetime('now'), lower(hex(randomblob(16)))),
('Bob', 'C', 'Johnson', '1978-03-10 00:00:00.000', 'Male', datetime('now'), datetime('now'), lower(hex(randomblob(16))));

INSERT INTO address (line_1, line_2, city, state_code, postal_code, dateutc_created, dateutc_modified)
VALUES
('123 Main St', NULL, 'Springfield', 'IL', '62704', datetime('now'), datetime('now')),
('456 Oak Ave', 'Apt 2B', 'Chicago', 'IL', '60601', datetime('now'), datetime('now'));

INSERT INTO person_address (person_id, address_id, is_primary, address_type_value, dateutc_created, dateutc_modified)
VALUES
(1, 1, 1, 4, datetime('now'), datetime('now')),
(2, 2, 1, 8, datetime('now'), datetime('now'));

INSERT INTO "User" ("Name", "Order", "Select")
VALUES
('AdminUser', 1, 1),
('RegularUser', 2, 0);
