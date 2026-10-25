-- For local demo only; two logically distinct databases on one PostgreSQL server.
CREATE DATABASE orders_db;
CREATE DATABASE inventory_db;
\connect orders_db
CREATE TABLE orders (
 id uuid PRIMARY KEY,
 tenant_id text NOT NULL,
 sku_id uuid NOT NULL,
 quantity integer NOT NULL CHECK(quantity > 0),
 status text NOT NULL,
 created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE idempotency_keys (
 tenant_id text NOT NULL,
 operation text NOT NULL,
 idempotency_key text NOT NULL,
 request_hash text NOT NULL,
 order_id uuid NOT NULL,
 PRIMARY KEY(tenant_id, operation, idempotency_key)
);
CREATE TABLE outbox_messages (
 id uuid PRIMARY KEY,
 event_type text NOT NULL,
 event_version integer NOT NULL,
 payload jsonb NOT NULL,
 created_at timestamptz NOT NULL DEFAULT now(),
 next_attempt_at timestamptz NOT NULL DEFAULT now(),
 published_at timestamptz,
 lease_id uuid,
 locked_until timestamptz,
 attempts integer NOT NULL DEFAULT 0,
 last_error text
);
CREATE INDEX ix_outbox_poll ON outbox_messages(next_attempt_at, created_at)
 WHERE published_at IS NULL;
\connect inventory_db
CREATE TABLE stock (
 sku_id uuid PRIMARY KEY,
 available integer NOT NULL CHECK(available>=0),
 reserved integer NOT NULL DEFAULT 0 CHECK(reserved>=0)
);
CREATE TABLE reservations (
 order_id uuid PRIMARY KEY,
 sku_id uuid NOT NULL,
 quantity integer NOT NULL,
 status text NOT NULL
);
CREATE TABLE inbox_messages (
 consumer_name text NOT NULL,
 message_id uuid NOT NULL,
 processed_at timestamptz NOT NULL DEFAULT now(),
 PRIMARY KEY(consumer_name, message_id)
);
INSERT INTO stock(sku_id, available) VALUES
 ('11111111-1111-1111-1111-111111111111', 10);
