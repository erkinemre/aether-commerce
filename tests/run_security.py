"""Run database-independent HTTP checks against real API processes."""
import os
from pathlib import Path
import secrets
import subprocess
import sys
import tempfile
import time
import urllib.request

root = Path(__file__).resolve().parents[1]
env = dict(os.environ)
env.update(CLIENT_API_KEY=secrets.token_hex(32), SERVICE_API_KEY=secrets.token_hex(32))
env.update(Security__ClientKey=env['CLIENT_API_KEY'], Security__ServiceKey=env['SERVICE_API_KEY'],
           Security__TenantId='demo', InventoryBaseUrl='http://127.0.0.1:15102',
           ORDERS_URL='http://127.0.0.1:15101', INVENTORY_URL='http://127.0.0.1:15102')
processes = []
try:
    with tempfile.TemporaryFile() as log:
        for service, port in [('Inventory', 15102), ('Orders', 15101)]:
            service_env = {**env, 'ASPNETCORE_URLS': f'http://127.0.0.1:{port}',
                           f'ConnectionStrings__{service}':
                           'Host=127.0.0.1;Port=1;Database=unavailable;Username=test;Timeout=1'}
            dll = root / f'src/Aether.{service}.Api/bin/Release/net10.0/Aether.{service}.Api.dll'
            processes.append(subprocess.Popen(['dotnet', str(dll)], env=service_env,
                stdout=log, stderr=log,
                creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0))
        for port in (15101, 15102):
            for attempt in range(60):
                try:
                    urllib.request.urlopen(f'http://127.0.0.1:{port}/health/live', timeout=1).close()
                    break
                except OSError:
                    time.sleep(.25)
            else:
                log.seek(0)
                sys.stdout.buffer.write(log.read())
                raise RuntimeError('API did not start')
        result = subprocess.run([sys.executable, str(root / 'tests/api_tests.py')], env=env)
        sys.exit(result.returncode)
finally:
    for process in processes:
        process.terminate()
    for process in processes:
        process.wait(timeout=10)
