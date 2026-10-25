"""Black-box checks. Integration mode requires a fresh, disposable Compose database."""
import concurrent.futures
import json
import os
import time
import unittest
import urllib.error
import urllib.request
import uuid

ORDERS = os.getenv("ORDERS_URL", "http://localhost:5101")
INVENTORY = os.getenv("INVENTORY_URL", "http://localhost:5102")
CLIENT = os.environ["CLIENT_API_KEY"]
SERVICE = os.environ["SERVICE_API_KEY"]
SKU = "11111111-1111-1111-1111-111111111111"
TENANT = os.getenv("TENANT_ID", "demo")

def request(base, path, body=None, key=None, idem=None):
    headers = {"Content-Type": "application/json"}
    if key is not None:
        headers["X-Api-Key"] = key
    if idem:
        headers["Idempotency-Key"] = idem
    req = urllib.request.Request(base + path,
        data=None if body is None else json.dumps(body).encode(), headers=headers)
    try:
        response = urllib.request.urlopen(req, timeout=10)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        data = response.read()
        return response.status, json.loads(data) if data else None

class SecurityTests(unittest.TestCase):
    def test_liveness(self):
        for base in (ORDERS, INVENTORY):
            self.assertEqual(200, request(base, "/health/live")[0])

    def test_missing_and_wrong_credentials(self):
        for key in (None, "incorrect"):
            self.assertEqual(401, request(ORDERS, "/orders", {}, key)[0])
            self.assertEqual(401, request(INVENTORY, "/internal/order-placed", {}, key)[0])

    def test_service_key_cannot_create_customer_order(self):
        self.assertEqual(401, request(ORDERS, "/orders", {}, SERVICE)[0])

    def test_cross_tenant_denied(self):
        body = {"tenantId": "another-tenant", "skuId": SKU, "quantity": 1}
        self.assertEqual(403, request(ORDERS, "/orders", body, CLIENT, "test")[0])
        self.assertEqual(403, request(ORDERS,
            f"/orders/{uuid.uuid4()}?tenantId=another-tenant", key=CLIENT)[0])

    def test_validation(self):
        body = {"tenantId": TENANT, "skuId": SKU, "quantity": 0}
        self.assertEqual(400, request(ORDERS, "/orders", body, CLIENT, "test")[0])
        body["quantity"] = 1
        self.assertEqual(400, request(ORDERS, "/orders", body, CLIENT)[0])
        self.assertEqual(400, request(INVENTORY, "/internal/order-placed", {}, SERVICE)[0])

@unittest.skipUnless(os.getenv("INTEGRATION") == "1", "requires disposable PostgreSQL fixture")
class CommerceTests(unittest.TestCase):
    def test_checkout_concurrency_idempotency_and_replay(self):
        body = {"tenantId": TENANT, "skuId": SKU, "quantity": 1}
        idem = str(uuid.uuid4())
        with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
            results = list(pool.map(lambda _: request(ORDERS, "/orders", body, CLIENT, idem), range(8)))
        self.assertEqual(1, sum(code == 201 for code, _ in results))
        self.assertTrue(all(code in (200, 201) for code, _ in results))
        self.assertEqual(1, len({result["orderId"] for _, result in results}))
        self.assertEqual(409, request(ORDERS, "/orders", {**body, "quantity": 2}, CLIENT, idem)[0])
        # More simultaneous orders than available stock: exactly ten can succeed.
        with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
            more = list(pool.map(lambda _: request(ORDERS, "/orders", body, CLIENT, str(uuid.uuid4())), range(14)))
        self.assertTrue(all(code == 201 for code, _ in more))
        ids = [results[0][1]["orderId"]] + [result["orderId"] for _, result in more]
        deadline = time.monotonic() + 90
        while True:
            states = [request(ORDERS, f"/orders/{oid}?tenantId={TENANT}", key=CLIENT)[1]["status"] for oid in ids]
            if "Pending" not in states:
                break
            if time.monotonic() > deadline:
                self.fail(f"Orders did not settle: {states}")
            time.sleep(10)
        self.assertEqual(10, states.count("Confirmed"))
        self.assertEqual(5, states.count("Rejected"))
        order = ids[states.index("Confirmed")]
        event = {"eventId": str(uuid.uuid4()), "orderId": order, "tenantId": TENANT,
                 "skuId": SKU, "quantity": 1, "occurredAt": "2026-01-01T00:00:00Z"}
        for _ in range(2):
            code, result = request(INVENTORY, "/internal/order-placed", event, SERVICE)
            self.assertEqual(200, code)
            self.assertEqual("Reserved", result["reservationStatus"])
        code, stock = request(INVENTORY, f"/internal/stock/{SKU}", key=SERVICE)
        self.assertEqual(200, code)
        self.assertEqual((0, 10), (stock["available"], stock["reserved"]))
        event["quantity"] = 2
        self.assertEqual(409, request(INVENTORY, "/internal/order-placed", event, SERVICE)[0])

if __name__ == "__main__":
    unittest.main(verbosity=2)
