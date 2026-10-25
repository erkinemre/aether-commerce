#!/usr/bin/env bash
set -euo pipefail
: "${CLIENT_API_KEY:?Set CLIENT_API_KEY}"
: "${SERVICE_API_KEY:?Set SERVICE_API_KEY}"
python3 tests/api_tests.py
