# Demo scenarios

Use a fresh disposable database for repeatable stock counts. Never reset a shared database to run these exercises.

1. **Access control:** `python tests/run_security.py` starts temporary APIs and verifies credentials, tenant boundaries and validation without a database.
2. **Order lifecycle:** create a two-unit order with a unique key. Follow Location using the client key until Confirmed. Internal stock inspection should report available 8, reserved 2 on a fresh fixture.
3. **Idempotency:** repeat the same key/body; verify the same order ID. Change quantity with the same key; expect 409. Use GET for current status.
4. **Outage:** `docker compose stop inventory`; submit an order. It remains Pending. `docker compose start inventory`; polling eventually shows the terminal outcome after backoff (up to five minutes).
5. **Concurrency and replay:** on a separate fresh fixture, export the two keys and run `INTEGRATION=1 python tests/api_tests.py`. It asserts one order for concurrent identical keys, ten confirmations and five rejections for fifteen one-unit orders, durable duplicate outcomes, unchanged stock on replay and 409 for conflicting quantity.
6. **Database readiness:** stop PostgreSQL in a disposable demo and check /health/ready returns 503 while /health/live remains 200. Restart it and check recovery.

Do not demonstrate a crash by mutating published outbox rows in valuable data. A future failure-injection test should pause between Inventory commit and Orders acknowledgement and verify automatic retry. That crash-window test is not yet automated.
