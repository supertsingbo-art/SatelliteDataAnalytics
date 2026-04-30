const fs = require("fs");
const path = require("path");
const os = require("os");
const { execFileSync } = require("child_process");
const docx = require("docx");

const {
    AlignmentType,
    BorderStyle,
    Document,
    HeadingLevel,
    ImageRun,
    LevelFormat,
    Packer,
    Paragraph,
    Table,
    TableCell,
    TableOfContents,
    TableRow,
    TextRun,
    WidthType,
} = docx;

const inputFile = process.argv[2];
const outputFile = process.argv[3];
const docTitle = process.argv[4] || "文档标题";

if (!inputFile || !outputFile) {
    console.error("Usage: node md2docx_with_mermaid.js <input.md> <output.docx> [title]");
    process.exit(1);
}

const BODY_SIZE = 22; // 11pt
const CODE_SIZE = 18; // 9pt
const MAX_IMAGE_WIDTH = 620;
const MAX_IMAGE_HEIGHT = 760;
const tempRoot = fs.mkdtempSync(path.join(os.tmpdir(), "satellite-docx-"));
const diagramDir = path.join(tempRoot, "diagrams");
fs.mkdirSync(diagramDir, { recursive: true });

function normalizePath(p) {
    return path.resolve(p);
}

function getMmdcPath() {
    const cmd = process.platform === "win32" ? "mmdc.cmd" : "mmdc";
    const local = path.join(process.cwd(), "node_modules", ".bin", cmd);
    return fs.existsSync(local) ? local : cmd;
}

function readPngSize(filePath) {
    const buffer = fs.readFileSync(filePath);
    if (buffer.length < 24 || buffer.toString("ascii", 1, 4) !== "PNG") {
        return { width: 560, height: 320 };
    }
    return {
        width: buffer.readUInt32BE(16),
        height: buffer.readUInt32BE(20),
    };
}

function createMermaidConfig() {
    const configPath = path.join(tempRoot, "mermaid-config.json");
    const config = {
        theme: "default",
        themeVariables: {
            fontFamily: "Microsoft YaHei, SimSun, Arial",
            fontSize: "14px",
            primaryColor: "#eef6ff",
            primaryBorderColor: "#3b82f6",
            lineColor: "#334155",
            textColor: "#111827",
        },
        flowchart: {
            htmlLabels: true,
            curve: "basis",
        },
        sequence: {
            actorFontSize: 14,
            noteFontSize: 14,
            messageFontSize: 14,
        },
        class: {
            fontSize: 14,
        },
    };
    fs.writeFileSync(configPath, JSON.stringify(config, null, 2), "utf-8");
    return configPath;
}

function renderMermaid(code, index) {
    const mmdPath = path.join(diagramDir, `diagram-${index}.mmd`);
    const pngPath = path.join(diagramDir, `diagram-${index}.png`);
    fs.writeFileSync(mmdPath, code, "utf-8");

    const mmdcArgs = [
        "-i", mmdPath,
        "-o", pngPath,
        "-b", "white",
        "-s", "2",
        "-c", createMermaidConfig(),
    ];
    const command = process.platform === "win32" ? "cmd.exe" : getMmdcPath();
    const args = process.platform === "win32" ? ["/c", getMmdcPath(), ...mmdcArgs] : mmdcArgs;

    execFileSync(command, args, {
        cwd: process.cwd(),
        stdio: "pipe",
        windowsHide: true,
    });

    const size = readPngSize(pngPath);
    const ratio = Math.min(1, MAX_IMAGE_WIDTH / size.width, MAX_IMAGE_HEIGHT / size.height);
    return {
        path: pngPath,
        width: Math.round(size.width * ratio),
        height: Math.round(size.height * ratio),
    };
}

function cleanInline(text) {
    return text.replace(/<br\s*\/?>/gi, "\n");
}

function createTextRuns(text, options = {}) {
    const runs = [];
    const regex = /(\*\*.*?\*\*|`.*?`)/g;
    let lastIndex = 0;
    let match;

    while ((match = regex.exec(text)) !== null) {
        if (match.index > lastIndex) {
            runs.push(new TextRun({
                text: cleanInline(text.substring(lastIndex, match.index)),
                size: options.size || BODY_SIZE,
                font: options.font || "Microsoft YaHei",
            }));
        }

        const part = match[0];
        if (part.startsWith("**") && part.endsWith("**")) {
            runs.push(new TextRun({
                text: cleanInline(part.substring(2, part.length - 2)),
                bold: true,
                size: options.size || BODY_SIZE,
                font: options.font || "Microsoft YaHei",
            }));
        } else if (part.startsWith("`") && part.endsWith("`")) {
            runs.push(new TextRun({
                text: part.substring(1, part.length - 1),
                font: "Consolas",
                color: "B91C1C",
                size: options.size || BODY_SIZE,
            }));
        }

        lastIndex = regex.lastIndex;
    }

    if (lastIndex < text.length) {
        runs.push(new TextRun({
            text: cleanInline(text.substring(lastIndex)),
            size: options.size || BODY_SIZE,
            font: options.font || "Microsoft YaHei",
        }));
    }

    return runs.length ? runs : [new TextRun({ text, size: options.size || BODY_SIZE, font: options.font || "Microsoft YaHei" })];
}

function isMarkdownTableSeparator(row) {
    return row
        .replace(/\|/g, "")
        .replace(/:/g, "")
        .replace(/-/g, "")
        .trim() === "";
}

function parseTableCells(row) {
    const trimmed = row.trim();
    const body = trimmed.startsWith("|") ? trimmed.slice(1) : trimmed;
    const withoutTail = body.endsWith("|") ? body.slice(0, -1) : body;
    return withoutTail.split("|").map((cell) => cell.trim());
}

function buildTable(tableRows) {
    const validRows = tableRows.filter((row) => !isMarkdownTableSeparator(row));
    const rows = validRows.map((row, rowIndex) => {
        const cells = parseTableCells(row);
        return new TableRow({
            children: cells.map((cell) => new TableCell({
                children: [new Paragraph({
                    children: createTextRuns(cell, { size: BODY_SIZE }),
                })],
                margins: { top: 80, bottom: 80, left: 80, right: 80 },
                shading: rowIndex === 0 ? { type: docx.ShadingType.CLEAR, fill: "E5E7EB" } : undefined,
            })),
        });
    });

    return new Table({
        rows,
        width: { size: 100, type: WidthType.PERCENTAGE },
        borders: {
            top: { style: BorderStyle.SINGLE, size: 1, color: "CBD5E1" },
            bottom: { style: BorderStyle.SINGLE, size: 1, color: "CBD5E1" },
            left: { style: BorderStyle.SINGLE, size: 1, color: "CBD5E1" },
            right: { style: BorderStyle.SINGLE, size: 1, color: "CBD5E1" },
            insideHorizontal: { style: BorderStyle.SINGLE, size: 1, color: "CBD5E1" },
            insideVertical: { style: BorderStyle.SINGLE, size: 1, color: "CBD5E1" },
        },
    });
}

function buildCodeBlock(code, language) {
    const lines = code.split("\n");
    const runs = lines.map((line, index) => new TextRun({
        text: line.length ? line : " ",
        font: "Consolas",
        size: CODE_SIZE,
        break: index === 0 ? 0 : 1,
    }));

    return [new Paragraph({
        children: runs,
        shading: { type: docx.ShadingType.CLEAR, fill: "F1F5F9" },
        spacing: { before: 120, after: 120, line: 240 },
    })];
}

function headingLevel(markCount) {
    if (markCount === 1) return HeadingLevel.TITLE;
    if (markCount === 2) return HeadingLevel.HEADING_1;
    if (markCount === 3) return HeadingLevel.HEADING_2;
    return HeadingLevel.HEADING_3;
}

function parseMarkdown(mdContent) {
    const lines = mdContent.split(/\r?\n/);
    const children = [];
    let tableRows = [];
    let inCode = false;
    let codeLanguage = "";
    let codeLines = [];
    let diagramIndex = 0;

    function flushTable() {
        if (tableRows.length > 0) {
            children.push(buildTable(tableRows));
            children.push(new Paragraph({ text: "" }));
            tableRows = [];
        }
    }

    function flushCode() {
        const code = codeLines.join("\n");
        if (codeLanguage.toLowerCase() === "mermaid") {
            diagramIndex += 1;
            const image = renderMermaid(code, diagramIndex);
            children.push(new Paragraph({
                children: [new TextRun({ text: `图 ${diagramIndex}`, bold: true, size: BODY_SIZE, font: "Microsoft YaHei" })],
                alignment: AlignmentType.CENTER,
                spacing: { before: 160, after: 80 },
            }));
            children.push(new Paragraph({
                children: [new ImageRun({
                    type: "png",
                    data: fs.readFileSync(image.path),
                    transformation: { width: image.width, height: image.height },
                })],
                alignment: AlignmentType.CENTER,
                spacing: { after: 160 },
            }));
        } else {
            children.push(...buildCodeBlock(code, codeLanguage));
        }

        codeLines = [];
        codeLanguage = "";
    }

    children.push(new Paragraph({
        text: "目录",
        heading: HeadingLevel.HEADING_1,
        alignment: AlignmentType.CENTER,
    }));
    children.push(new TableOfContents("目录", { hyperlink: true, headingStyleRange: "1-4" }));
    children.push(new Paragraph({ text: "", pageBreakBefore: true }));

    for (const rawLine of lines) {
        const line = rawLine.replace(/\s+$/g, "");
        const trimmed = line.trim();
        const fence = trimmed.match(/^```(.*)$/);

        if (fence) {
            if (inCode) {
                inCode = false;
                flushCode();
            } else {
                flushTable();
                inCode = true;
                codeLanguage = (fence[1] || "").trim();
                codeLines = [];
            }
            continue;
        }

        if (inCode) {
            codeLines.push(rawLine);
            continue;
        }

        if (trimmed.startsWith("|")) {
            tableRows.push(trimmed);
            continue;
        }
        flushTable();

        if (!trimmed) {
            children.push(new Paragraph({ text: "" }));
            continue;
        }

        if (trimmed === "---") {
            children.push(new Paragraph({ text: "" }));
            continue;
        }

        const heading = trimmed.match(/^(#{1,6})\s+(.*)$/);
        if (heading) {
            const markCount = heading[1].length;
            children.push(new Paragraph({
                children: createTextRuns(heading[2], {
                    size: markCount === 1 ? 32 : markCount === 2 ? 28 : markCount === 3 ? 24 : BODY_SIZE,
                }),
                heading: headingLevel(markCount),
                spacing: { before: 240, after: 120 },
            }));
            continue;
        }

        if (trimmed.startsWith("- ")) {
            children.push(new Paragraph({
                children: createTextRuns(trimmed.substring(2)),
                bullet: { level: 0 },
                spacing: { after: 60 },
            }));
            continue;
        }

        const numbered = trimmed.match(/^(\d+)\.\s+(.*)$/);
        if (numbered) {
            children.push(new Paragraph({
                children: createTextRuns(numbered[2]),
                numbering: { reference: "ordered-list", level: 0 },
                spacing: { after: 60 },
            }));
            continue;
        }

        if (trimmed.startsWith("> ")) {
            children.push(new Paragraph({
                children: createTextRuns(trimmed.substring(2)),
                indent: { left: 420 },
                shading: { type: docx.ShadingType.CLEAR, fill: "F8FAFC" },
            }));
            continue;
        }

        children.push(new Paragraph({
            children: createTextRuns(trimmed),
            spacing: { after: 80 },
        }));
    }

    flushTable();
    if (inCode) flushCode();

    return children;
}

async function main() {
    const mdContent = fs.readFileSync(normalizePath(inputFile), "utf-8");
    const children = parseMarkdown(mdContent);

    const doc = new Document({
        creator: "Cursor AI",
        title: docTitle,
        description: docTitle,
        styles: {
            default: {
                document: {
                    run: {
                        font: "Microsoft YaHei",
                        size: BODY_SIZE,
                    },
                    paragraph: {
                        spacing: { line: 360 },
                    },
                },
            },
            paragraphStyles: [
                {
                    id: "Title",
                    name: "Title",
                    basedOn: "Normal",
                    next: "Normal",
                    quickFormat: true,
                    run: { size: 36, bold: true, font: "Microsoft YaHei" },
                    paragraph: { alignment: AlignmentType.CENTER, spacing: { before: 240, after: 240 } },
                },
                {
                    id: "Heading1",
                    name: "Heading 1",
                    basedOn: "Normal",
                    next: "Normal",
                    quickFormat: true,
                    run: { size: 32, bold: true, font: "Microsoft YaHei", color: "000000" },
                    paragraph: { spacing: { before: 300, after: 160 }, outlineLevel: 0 },
                },
                {
                    id: "Heading2",
                    name: "Heading 2",
                    basedOn: "Normal",
                    next: "Normal",
                    quickFormat: true,
                    run: { size: 28, bold: true, font: "Microsoft YaHei", color: "000000" },
                    paragraph: { spacing: { before: 240, after: 120 }, outlineLevel: 1 },
                },
                {
                    id: "Heading3",
                    name: "Heading 3",
                    basedOn: "Normal",
                    next: "Normal",
                    quickFormat: true,
                    run: { size: 24, bold: true, font: "Microsoft YaHei", color: "000000" },
                    paragraph: { spacing: { before: 200, after: 100 }, outlineLevel: 2 },
                },
            ],
        },
        numbering: {
            config: [
                {
                    reference: "ordered-list",
                    levels: [
                        {
                            level: 0,
                            format: LevelFormat.DECIMAL,
                            text: "%1.",
                            alignment: AlignmentType.START,
                            style: {
                                paragraph: { indent: { left: 720, hanging: 360 } },
                                run: { size: BODY_SIZE, font: "Microsoft YaHei" },
                            },
                        },
                    ],
                },
            ],
        },
        sections: [{
            properties: {
                page: {
                    margin: {
                        top: 1440,
                        right: 1440,
                        bottom: 1440,
                        left: 1440,
                    },
                },
            },
            children,
        }],
    });

    const buffer = await Packer.toBuffer(doc);
    fs.writeFileSync(normalizePath(outputFile), buffer);
    console.log(`Conversion successful: ${outputFile}`);
    console.log(`Temporary rendered diagrams: ${diagramDir}`);
}

main().catch((error) => {
    console.error(error);
    process.exit(1);
});
