import os
import socket

import requests


def show(label: str, fn) -> None:
    print(f"[{label}]")
    try:
        result = fn()
        print(result)
    except Exception as exc:  # noqa: BLE001
        print(f"{type(exc).__name__}: {exc}")


print("[env]")
for key in ("HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY"):
    print(f"{key}={os.getenv(key, '')}")

print("[dns]")
for host_name in ("host.docker.internal", "gateway.docker.internal"):
    try:
        print(f"{host_name}={socket.gethostbyname(host_name)}")
    except Exception as exc:  # noqa: BLE001
        print(f"{host_name}={type(exc).__name__}: {exc}")

print("[socket-candidates]")
for candidate in (
    "host.docker.internal",
    "gateway.docker.internal",
    "172.26.176.1",
    "172.20.16.1",
    "192.168.1.11",
):
    try:
        socket.create_connection((candidate, 10808), timeout=5).close()
        print(f"{candidate}=connected")
    except Exception as exc:  # noqa: BLE001
        print(f"{candidate}={type(exc).__name__}: {exc}")

show(
    "socket-host-proxy",
    lambda: (
        socket.create_connection(("host.docker.internal", 10808), timeout=5).close()
        or "connected"
    ),
)

show(
    "socket-gateway-proxy",
    lambda: (
        socket.create_connection(("gateway.docker.internal", 10808), timeout=5).close()
        or "connected"
    ),
)

show(
    "httpbin",
    lambda: requests.get("https://httpbin.org/ip", timeout=15).text,
)

show(
    "eastmoney",
    lambda: requests.get(
        "https://82.push2.eastmoney.com/api/qt/clist/get",
        params={
            "pn": 1,
            "pz": 2,
            "po": 1,
            "np": 1,
            "ut": "bd1d9ddb04089700cf9c27f6f7426281",
            "fltt": 2,
            "invt": 2,
            "fid": "f12",
            "fs": "m:0+t:6,m:0+t:80,m:1+t:2,m:1+t:23,m:0+t:81+s:2048",
            "fields": "f12,f14",
        },
        timeout=15,
    ).text[:500],
)
