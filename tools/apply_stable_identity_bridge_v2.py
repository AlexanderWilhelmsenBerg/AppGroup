from pathlib import Path
import re

script_path = Path(__file__).with_name("apply_stable_identity_bridge.py")
source = script_path.read_text(encoding="utf-8")
replacement = "legacy_items_pattern = r'''                            if \\(paths != null\\) \\{.*?(?=                        \\}\\n                        else \\{)'''"
source, count = re.subn(
    r"legacy_items_pattern = r''' .*?'''".replace(" ", ""),
    replacement,
    source,
    count=1,
    flags=re.S,
)
if count != 1:
    # Match the assignment independently of its original expression contents.
    source, count = re.subn(
        r"legacy_items_pattern\s*=\s*r'''[\s\S]*?'''",
        replacement,
        source,
        count=1,
    )
if count != 1:
    raise RuntimeError(f"Unable to replace legacy editor item pattern: {count}")

exec(compile(source, str(script_path), "exec"), {"__name__": "__main__", "__file__": str(script_path)})
