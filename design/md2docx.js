const fs = require('fs');
const docx = require('docx');
const { Document, Packer, Paragraph, TextRun, HeadingLevel, TableOfContents, Table, TableRow, TableCell, WidthType, BorderStyle, AlignmentType } = docx;

const inputFile = process.argv[2];
const outputFile = process.argv[3];
const docTitle = process.argv[4] || "文档标题";

if (!inputFile || !outputFile) {
    console.error("Usage: node md2docx.js <input.md> <output.docx> [title]");
    process.exit(1);
}

const mdContent = fs.readFileSync(inputFile, 'utf-8');
const lines = mdContent.split(/\r?\n/);

const children = [];

// Create Table of Contents
children.push(new Paragraph({
    text: "目录",
    heading: HeadingLevel.HEADING_1,
    alignment: AlignmentType.CENTER
}));

children.push(new TableOfContents("目录", {
    hyperlink: true,
    headingStyleRange: "1-4",
}));

children.push(new Paragraph({
    text: "",
    pageBreakBefore: true
}));

let inTable = false;
let tableRows = [];

function createTextRuns(text) {
    const runs = [];
    const regex = /(\*\*.*?\*\*|`.*?`)/g;
    let lastIndex = 0;
    
    let match;
    while ((match = regex.exec(text)) !== null) {
        if (match.index > lastIndex) {
            runs.push(new TextRun({ text: text.substring(lastIndex, match.index) }));
        }
        
        const part = match[0];
        if (part.startsWith('**') && part.endsWith('**')) {
            runs.push(new TextRun({ text: part.substring(2, part.length - 2), bold: true }));
        } else if (part.startsWith('`') && part.endsWith('`')) {
            runs.push(new TextRun({ text: part.substring(1, part.length - 1), font: "Courier New", color: "dd1144" }));
        }
        
        lastIndex = regex.lastIndex;
    }
    
    if (lastIndex < text.length) {
        runs.push(new TextRun({ text: text.substring(lastIndex) }));
    }
    
    if (runs.length === 0) {
        runs.push(new TextRun({ text: text }));
    }
    
    return runs;
}

function processTable() {
    if (tableRows.length > 0) {
        const validRows = tableRows.filter(r => r.replace(/\|/g, '').replace(/-/g, '').trim() !== '');
        if (validRows.length > 0) {
            const rows = validRows.map((r, i) => {
                const cells = r.split('|').filter((_, index, arr) => index > 0 && index < arr.length - 1).map(c => c.trim());
                return new TableRow({
                    children: cells.map(c => new TableCell({
                        children: [new Paragraph({ children: createTextRuns(c) })],
                        margins: { top: 100, bottom: 100, left: 100, right: 100 }
                    }))
                });
            });
            children.push(new Table({ 
                rows, 
                width: { size: 100, type: WidthType.PERCENTAGE },
                borders: {
                    top: { style: BorderStyle.SINGLE, size: 1 },
                    bottom: { style: BorderStyle.SINGLE, size: 1 },
                    left: { style: BorderStyle.SINGLE, size: 1 },
                    right: { style: BorderStyle.SINGLE, size: 1 },
                    insideHorizontal: { style: BorderStyle.SINGLE, size: 1 },
                    insideVertical: { style: BorderStyle.SINGLE, size: 1 },
                }
            }));
        }
        tableRows = [];
    }
}

let inCodeBlock = false;
let codeContent = [];

for (let i = 0; i < lines.length; i++) {
    let line = lines[i];
    let trimmedLine = line.trim();

    if (trimmedLine.startsWith('```')) {
        if (inCodeBlock) {
            inCodeBlock = false;
            children.push(new Paragraph({
                children: [new TextRun({ text: codeContent.join('\n'), font: "Courier New", size: 20 })],
                shading: { type: docx.ShadingType.CLEAR, fill: "f1f5f9" }
            }));
            codeContent = [];
        } else {
            inCodeBlock = true;
        }
        continue;
    }

    if (inCodeBlock) {
        codeContent.push(line);
        continue;
    }

    if (trimmedLine.startsWith('|')) {
        inTable = true;
        tableRows.push(trimmedLine);
        continue;
    } else if (inTable) {
        inTable = false;
        processTable();
    }

    if (!trimmedLine) {
        children.push(new Paragraph({ text: "" }));
        continue;
    }

    if (trimmedLine.startsWith('# ')) {
        children.push(new Paragraph({ children: createTextRuns(trimmedLine.substring(2)), heading: HeadingLevel.HEADING_1 }));
    } else if (trimmedLine.startsWith('## ')) {
        children.push(new Paragraph({ children: createTextRuns(trimmedLine.substring(3)), heading: HeadingLevel.HEADING_2 }));
    } else if (trimmedLine.startsWith('### ')) {
        children.push(new Paragraph({ children: createTextRuns(trimmedLine.substring(4)), heading: HeadingLevel.HEADING_3 }));
    } else if (trimmedLine.startsWith('#### ')) {
        children.push(new Paragraph({ children: createTextRuns(trimmedLine.substring(5)), heading: HeadingLevel.HEADING_4 }));
    } else if (trimmedLine.startsWith('- ')) {
        children.push(new Paragraph({ children: createTextRuns(trimmedLine.substring(2)), bullet: { level: 0 } }));
    } else if (trimmedLine.match(/^\d+\.\s/)) {
        children.push(new Paragraph({ children: createTextRuns(trimmedLine) }));
    } else if (trimmedLine.startsWith('> ')) {
        children.push(new Paragraph({ children: createTextRuns(trimmedLine.substring(2)), indent: { left: 720 }, shading: { type: docx.ShadingType.CLEAR, fill: "f8fafc" } }));
    } else {
        children.push(new Paragraph({ children: createTextRuns(trimmedLine) }));
    }
}
if (inTable) processTable();

const doc = new Document({
    creator: "Cursor AI",
    title: docTitle,
    description: docTitle,
    styles: {
        paragraphStyles: [
            {
                id: "Heading1",
                name: "Heading 1",
                basedOn: "Normal",
                next: "Normal",
                quickFormat: true,
                run: { size: 32, bold: true, color: "000000" },
                paragraph: { spacing: { before: 240, after: 120 } }
            },
            {
                id: "Heading2",
                name: "Heading 2",
                basedOn: "Normal",
                next: "Normal",
                quickFormat: true,
                run: { size: 28, bold: true, color: "000000" },
                paragraph: { spacing: { before: 240, after: 120 } }
            },
            {
                id: "Heading3",
                name: "Heading 3",
                basedOn: "Normal",
                next: "Normal",
                quickFormat: true,
                run: { size: 24, bold: true, color: "000000" },
                paragraph: { spacing: { before: 240, after: 120 } }
            }
        ]
    },
    sections: [{
        properties: {},
        children
    }]
});

Packer.toBuffer(doc).then((buffer) => {
    fs.writeFileSync(outputFile, buffer);
    console.log(`Conversion successful: ${outputFile}`);
});