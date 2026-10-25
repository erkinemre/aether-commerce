# Verification status

See [assessment](../docs/assessment.md) for actual executed results and limitations. Static inspection does not establish database concurrency correctness. `api_tests.py` contains assertions; `run_security.py` runs the database-independent subset against real API processes. Integration mode is opt-in and consumes the disposable seed.
