#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
将 design/详细设计文档_卫星测试数据预处理与数据分析平台_V2.0.md 转为 Word（.docx）。

- 所有 ```mermaid ... ``` 代码块经 Kroki 服务渲染为 PNG，再嵌入 Markdown，保证图中文字与 Mermaid 语义一致。
- 需要本机已安装 pandoc（https://pandoc.org/installing.html），并需可访问 https://kroki.io/。

用法（在仓库根目录或 design 目录执行均可）:
  python design/tools/export_detail_design_docx.py
"""

from __future__ import annotations

import json
import re
import subprocess
import sys
import urllib.error
import urllib.request
from pathlib import Path

DESIGN_DIR = Path(__file__).resolve().parent.parent
MD_NAME = "详细设计文档_卫星测试数据预处理与数据分析平台_V2.0.md"
SOURCE_MD = DESIGN_DIR / MD_NAME
BUILD_DIR = DESIGN_DIR / "tools" / "_docx_build"
OUTPUT_DOCX = DESIGN_DIR / "详细设计文档_卫星测试数据预处理与数据分析平台_V2.0.docx"
KROKI_URL = "https://kroki.io/"


def kroki_mermaid_png(diagram_source: str) -> bytes:
    payload = json.dumps(
        {
            "diagram_source": diagram_source.strip(),
            "diagram_type": "mermaid",
            "output_format": "png",
        },
        ensure_ascii=False,
    ).encode("utf-8")
    req = urllib.request.Request(
        KROKI_URL,
        data=payload,
        headers={"Content-Type": "application/json; charset=utf-8"},
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=180) as resp:
        return resp.read()


def main() -> int:
    if not SOURCE_MD.is_file():
        print(f"找不到源文件: {SOURCE_MD}", file=sys.stderr)
        return 1

    text = SOURCE_MD.read_text(encoding="utf-8")
    pattern = re.compile(r"```mermaid\n(.*?)```", re.DOTALL)

    BUILD_DIR.mkdir(parents=True, exist_ok=True)

    counter = 0

    def replace_block(m: re.Match[str]) -> str:
        nonlocal counter
        counter += 1
        body = m.group(1).strip()
        fname = f"mermaid_{counter:02d}.png"
        out_png = BUILD_DIR / fname
        try:
            png = kroki_mermaid_png(body)
        except urllib.error.HTTPError as e:
            print(f"Kroki HTTP {e.code} for block #{counter}: {e.read()[:500]!r}", file=sys.stderr)
            raise
        out_png.write_bytes(png)
        # 与临时 md 同目录引用，便于 pandoc 解析
        return f"![mermaid-{counter:02d}]({fname})\n\n"

    new_text, n = pattern.subn(replace_block, text)
    if n == 0:
        print("未找到 mermaid 代码块，仍将尝试生成 docx。")

    body_md = BUILD_DIR / "_export_body.md"
    body_md.write_text(new_text, encoding="utf-8")

    try:
        subprocess.run(
            [
                "pandoc",
                str(body_md.name),
                "-o",
                str(OUTPUT_DOCX.resolve()),
                "-f",
                "markdown",
                "-t",
                "docx",
                "--standalone",
            ],
            cwd=str(BUILD_DIR),
            check=True,
        )
    except FileNotFoundError:
        print(
            "未找到 pandoc。请先安装: https://pandoc.org/installing.html\n"
            "Windows 可: winget install --id JohnMacFarlane.Pandoc",
            file=sys.stderr,
        )
        return 2
    except subprocess.CalledProcessError as e:
        print(f"pandoc 失败: {e}", file=sys.stderr)
        return 3

    print(f"已生成: {OUTPUT_DOCX}")
    print(f"Mermaid 图块数: {n}，PNG 目录: {BUILD_DIR}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
