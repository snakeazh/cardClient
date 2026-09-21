-- Run as a PostgreSQL superuser (default: postgres) against the postgres database.
-- Existing user/database errors can be ignored.

CREATE USER card WITH PASSWORD 'card';
CREATE DATABASE card OWNER card;
GRANT ALL PRIVILEGES ON DATABASE card TO card;
