#!/usr/bin/env python3
"""Copy Unity generated config classes into CardShare.Contracts (no UnityEngine)."""
from pathlib import Path
import re

src = Path(r"d:\Work\cardClient\Card\Assets\App\Config\Generated")
dst = Path(r"d:\Work\cardClient\Server\src\Card.Contracts\Config")
dst.mkdir(parents=True, exist_ok=True)

skip = {"ConfigTables.cs", "ResResourcePaths.Config.g.cs"}

for path in sorted(src.glob("*.cs")):
    if path.name in skip:
        continue
    original = path.read_text(encoding="utf-8")
    text = original.replace("namespace App.Config", "namespace CardShare.Contracts.Config")
    if path.name == "Enums.g.cs":
        (dst / path.name).write_text(text, encoding="utf-8", newline="\n")
        print("copied", path.name)
        continue

    name = path.stem
    is_row = f" : ConfigRowBase<{name}>" in original
    is_const = f" : ConfigConstBase<{name}>" in original
    text = text.replace(f" : ConfigRowBase<{name}>", "")
    text = text.replace(f" : ConfigConstBase<{name}>", "")

    if is_row:
        text = text.replace(
            f"    public sealed class {name}\n    {{\n",
            f"    public sealed class {name}\n    {{\n        public int Id;\n\n",
        )

    if is_const:
        inject = f"""    public sealed class {name}
    {{
        public static {name} Instance {{ get; private set; }} = new {name}();

        public static void Load({name} data)
        {{
            Instance = data ?? new {name}();
        }}

"""
        text = re.sub(
            rf"    public sealed class {name}\n    \{{\n",
            inject,
            text,
            count=1,
        )

    (dst / path.name).write_text(text, encoding="utf-8", newline="\n")
    print("copied", path.name)
