"""Inventory demo bytes, excluding the inventory itself."""
import hashlib
from pathlib import Path

root = Path(__file__).resolve().parents[1] / "demo"
lines = []
for path in sorted(root.rglob("*")):
    if path.is_file() and path.name != "SHA256SUMS.txt":
        lines.append(f"{hashlib.sha256(path.read_bytes()).hexdigest()}  {path.relative_to(root).as_posix()}")
(root / "SHA256SUMS.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
print(f"Inventoried {len(lines)} demo files")
