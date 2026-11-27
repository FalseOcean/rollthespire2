"""Pre-draw a new 100-run manifest for Run.ps1 -Seeds. Retain it for every retry."""
import argparse
import hashlib
import json
import secrets
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("manifest", type=Path, help="Path to a new JSON file")
    path = parser.parse_args().manifest
    alphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ"
    samples = {}
    while len(samples) < 100:
        seed = "".join(secrets.choice(alphabet) for _ in range(12))
        if seed not in samples:
            samples[seed] = secrets.randbelow(2**31 - 1)
    payload = json.dumps([{"seed": seed, "actionSeed": action} for seed, action in samples.items()], indent=2) + "\n"
    data = payload.encode("utf-8")
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("xb") as output:
        output.write(data)
    print(f"{hashlib.sha256(data).hexdigest()}  {path}")


if __name__ == "__main__":
    main()
