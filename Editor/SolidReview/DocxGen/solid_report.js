#!/usr/bin/env node
// solid_report.js
// Called by SolidReportExporter.cs via Process.Start
// Args: solid_report.js <mode> <outputPath> <jsonDataPath>
//   mode = "summary" | "file"
//
// Reads JSON from jsonDataPath, writes .docx to outputPath, exits 0 on success.

"use strict";
const path = require("path");
const fs   = require("fs");

// Locate the docx package. Checked in order:
// 1. node_modules~/ next to this script (bundled, Unity-safe tilde name)
// 2. node_modules/ next to this script (plain bundled)
// 3. Library/DocxGen/node_modules/ (auto-installed by SolidReportExporter.cs)
function loadDocx() {
  const candidates = [
    path.join(__dirname, "node_modules~", "docx"),
    path.join(__dirname, "node_modules", "docx"),
    // Walk up to find Library/DocxGen relative to the package cache path
    // __dirname = …/Library/PackageCache/com.gamedistrict.codeshield@xxx/Editor/SolidReview/DocxGen
    // Library   = …/Library/
    path.join(__dirname, "..", "..", "..", "..", "..", "DocxGen", "node_modules", "docx"),
  ];

  for (const p of candidates) {
    if (fs.existsSync(p)) return require(p);
  }

  throw new Error(
    "Cannot find docx package.\n" +
    "Checked:\n" + candidates.join("\n") + "\n\n" +
    "Fix: click Export again — it will run 'npm install docx' automatically.\n" +
    "Or run manually: cd \"" + __dirname + "\" && npm install docx"
  );
}

const {
  Document, Packer, Paragraph, TextRun, Table, TableRow, TableCell,
  PageBreak, AlignmentType, BorderStyle, WidthType, ShadingType,
  VerticalAlign, HeadingLevel, LevelFormat, Header, Footer, PageNumber,
  NumberFormat
} = loadDocx();

// ── Colours (hex without #) ──────────────────────────────────────────────────
// LIGHT THEME — designed for white page background, readable when printed.
// GD yellow stays as the brand accent for headings & callouts.
const GD_YELLOW   = "C99700";  // darker yellow — readable on white
const GD_ACCENT   = "FFC700";  // bright accent for fills (borders, pill backgrounds)
const GD_PAGE_BG  = "FFFFFF";  // page background (white)
const GD_CARD_BG  = "F4F4F4";  // soft gray card surface
const GD_CARD_BG2 = "EAEAEA";  // slightly darker card variant
const GD_HEADER_BG= "FFF5D6";  // pale yellow header rows
const GD_TEXT     = "1A1A1A";  // primary text color
const GD_TEXT_SUB = "4A4A4A";  // secondary text
const GD_MUTED    = "7A7A7A";  // muted/quiet text
const GD_BORDER   = "D6D6D6";  // table cell border
const C_GREEN     = "1F9D55";  // print-safe green
const C_BLUE      = "1A6FB5";  // print-safe blue
const C_ORANGE    = "D97706";  // print-safe orange (mid)
const C_RED_WEAK  = "CC6628";  // print-safe orange (weak score)
const C_RED_POOR  = "B91C1C";  // print-safe red
const C_RED_HIGH  = "B91C1C";
const C_ORANGE_MED= "D97706";
const C_GREEN_LOW = "1F9D55";

// Legacy aliases kept so the rest of the file compiles without rewrites
const GD_DARK     = GD_CARD_BG;
const GD_DARK2    = GD_CARD_BG2;
const GD_WHITE    = GD_TEXT;

function scoreColor(score) {
  if (score >= 4.5) return C_GREEN;
  if (score >= 3.5) return C_BLUE;
  if (score >= 2.5) return C_ORANGE;
  if (score >= 1.5) return C_RED_WEAK;
  return C_RED_POOR;
}
function sevColor(sev) {
  if (sev === "High")   return C_RED_HIGH;
  if (sev === "Medium") return C_ORANGE_MED;
  return C_GREEN_LOW;
}
// Score quality words. These are descriptive but DO NOT communicate target gap —
// see scoreStatus() for the target-aware version used in the report.
function scoreLabel(score) {
  if (score >= 4.5) return "Excellent";
  if (score >= 3.5) return "Very Good";
  if (score >= 2.5) return "Acceptable";
  if (score >= 1.5) return "Weak";
  return "Poor";
}

// Target is 4.0 — anything below should signal action, not "acceptable".
const SCORE_TARGET = 4.0;
function scoreStatus(score) {
  if (score >= 4.5) return "On Target";
  if (score >= 4.0) return "Meets Target";
  if (score >= 3.0) return "Below Target";
  if (score >= 2.0) return "Needs Work";
  return "Critical";
}
function isBelowTarget(score) {
  return score < SCORE_TARGET;
}

// Group violations by normalised title so we don't print 11 identical "X has multiple responsibilities" lines.
// Returns array of { title, severity, description, evidence, locations: [{fileName, startLine}], count }
// Title patterns like "AnalyticsManager has multiple responsibilities" are normalised to "Class has multiple responsibilities"
// so duplicates collapse, with the original class name preserved in each location.
function groupViolations(violations) {
  const groups = new Map();
  for (const v of violations) {
    const key = normaliseTitle(v.title || "") + "|" + (v.severity || "");
    const loc = {
      className: extractClassName(v.title || "", v.location?.fileName || ""),
      fileName: v.location?.fileName || "",
      startLine: v.location?.startLine || 0,
    };
    if (groups.has(key)) {
      const g = groups.get(key);
      g.locations.push(loc);
      g.count++;
    } else {
      groups.set(key, {
        title: genericTitle(v.title || ""),
        severity: v.severity || "Low",
        description: v.description || "",
        evidence: v.evidence || "",
        locations: [loc],
        count: 1,
      });
    }
  }
  return [...groups.values()];
}

// "AnalyticsManager has multiple responsibilities" → "X has multiple responsibilities"
function normaliseTitle(title) {
  return title
    .replace(/^[A-Za-z_][\w]*\s+/, "X ")            // leading ClassName → "X"
    .replace(/\s+[A-Za-z_][\w]+\s+method/, " X method")
    .toLowerCase();
}

// Pretty version of the generic title shown to user
function genericTitle(title) {
  return title.replace(/^[A-Za-z_][\w]*(\s+)/, "Class$1");
}

// Pull class name out of the violation title (or fall back to file name without extension)
function extractClassName(title, fileName) {
  const m = title.match(/^([A-Za-z_][\w]*)\s+/);
  if (m) return m[1];
  return (fileName || "").replace(/\.cs$/i, "");
}

// ── Helpers ──────────────────────────────────────────────────────────────────
const noBorder = { style: BorderStyle.NONE, size: 0, color: "FFFFFF" };
const noBorders = { top: noBorder, bottom: noBorder, left: noBorder, right: noBorder };
const thinBorder = { style: BorderStyle.SINGLE, size: 4, color: GD_BORDER };
const thinBorders = { top: thinBorder, bottom: thinBorder, left: thinBorder, right: thinBorder };

// Full-width = A4 minus 1" margins = 11906 - 2*1440 = 9026 DXA
const PAGE_W = 9026;

function run(text, opts = {}) {
  return new TextRun({
    text,
    font: "Calibri",
    color: opts.color || GD_TEXT,
    bold: opts.bold || false,
    size: (opts.size || 11) * 2,
    highlight: opts.highlight || undefined,
  });
}

function para(children, opts = {}) {
  return new Paragraph({
    alignment: opts.align || AlignmentType.LEFT,
    spacing: { before: opts.before ?? 0, after: opts.after ?? 80 },
    shading: opts.bg ? { fill: opts.bg, type: ShadingType.CLEAR } : undefined,
    indent: opts.indent ? { left: opts.indent } : undefined,
    border: opts.borderBottom ? {
      bottom: { style: BorderStyle.SINGLE, size: 6, color: opts.borderBottom, space: 1 }
    } : undefined,
    children: Array.isArray(children) ? children : [children],
  });
}

function pageBreak() {
  return new Paragraph({ children: [new PageBreak()] });
}

// Yellow section heading (like "S — Single Responsibility")
function sectionHeading(text, color) {
  return para([
    run(text, { bold: true, size: 14, color: color || GD_YELLOW })
  ], { before: 200, after: 120, borderBottom: color || GD_YELLOW });
}

// Small caps label (like "SCORES AT A GLANCE")
function capsLabel(text) {
  return para([run(text, { bold: true, size: 9, color: GD_YELLOW })], { before: 160, after: 40 });
}

// Table cell helper
function cell(children, opts = {}) {
  return new TableCell({
    width: opts.width ? { size: opts.width, type: WidthType.DXA } : undefined,
    shading: opts.bg ? { fill: opts.bg, type: ShadingType.CLEAR } : { fill: GD_DARK2, type: ShadingType.CLEAR },
    borders: opts.borders || thinBorders,
    margins: { top: 60, bottom: 60, left: 120, right: 120 },
    verticalAlign: VerticalAlign.CENTER,
    children: Array.isArray(children) ? children : [children],
  });
}

function headerRow(labels, widths) {
  return new TableRow({
    tableHeader: true,
    children: labels.map((lbl, i) =>
      cell(
        para([run(lbl, { bold: true, size: 10, color: GD_YELLOW })]),
        { bg: GD_HEADER_BG, width: widths[i], borders: thinBorders }
      )
    )
  });
}

function scoresAtAGlanceTable(ratings) {
  const W = [3800, 1200, 4026]; // Principle | Score | Short version
  return new Table({
    width: { size: PAGE_W, type: WidthType.DXA },
    columnWidths: W,
    rows: [
      headerRow(["Principle", "Score", "Short Version"], W),
      ...ratings.map(r => {
        const col = scoreColor(r.score);
        const lbl = scoreLabel(r.score);
        return new TableRow({ children: [
          cell(para([run(`${r.principle}`, { bold: true, size: 10, color: col })]), { width: W[0] }),
          cell(para([run(`${r.score} / 5`, { bold: true, size: 10, color: col })]), { width: W[1] }),
          cell(para([run(r.reason || "", { size: 10, color: GD_MUTED })]), { width: W[2] }),
        ]});
      })
    ]
  });
}

// ── SUMMARY REPORT ────────────────────────────────────────────────────────────
function buildSummary(data) {
  const children = [];
  const date = data.generatedAt || new Date().toISOString().slice(0,10);
  const gameName = data.gameName || data.projectName || "Unity Project";
  const bundleId = data.bundleId || "";

  // ── Cover heading
  children.push(para([
    run(`SOLID Review  —  ${gameName}`, { bold: true, size: 22, color: GD_YELLOW })
  ], { before: 0, after: 40 }));
  if (bundleId) {
    children.push(para([
      run("Bundle ID: ", { size: 10, color: GD_MUTED }),
      run(bundleId, { size: 10, color: GD_TEXT_SUB, bold: true }),
    ], { after: 20 }));
  }
  children.push(para([run(date, { size: 10, color: GD_MUTED })], { after: 200 }));

  // ── Stats row: 3-col table
  const SW = [3000, 3000, 3026];
  children.push(new Table({
    width: { size: PAGE_W, type: WidthType.DXA },
    columnWidths: SW,
    rows: [new TableRow({ children: [
      cell([
        para([run("FILES SCANNED", { bold: true, size: 9, color: GD_YELLOW })], { after: 20 }),
        para([run(`${data.totalFiles}`, { bold: true, size: 28, color: GD_WHITE })], { after: 0 }),
      ], { bg: GD_DARK, width: SW[0], borders: noBorders }),
      cell([
        para([run("TOTAL VIOLATIONS", { bold: true, size: 9, color: GD_YELLOW })], { after: 20 }),
        para([run(`${data.totalViolations}`, { bold: true, size: 28, color: GD_WHITE })], { after: 0 }),
      ], { bg: GD_DARK, width: SW[1], borders: noBorders }),
      cell([
        para([run("DATE", { bold: true, size: 9, color: GD_YELLOW })], { after: 20 }),
        para([run(date, { size: 12, color: GD_WHITE })], { after: 0 }),
      ], { bg: GD_DARK, width: SW[2], borders: noBorders }),
    ]})]
  }));
  children.push(para([], { before: 120, after: 120 }));

  // ── Overall score
  const oc = scoreColor(data.overallScore);
  const status = scoreStatus(data.overallScore);
  children.push(capsLabel("OVERALL SCORE"));
  children.push(para([
    run(`${parseFloat(data.overallScore).toFixed(1)}`, { bold: true, size: 36, color: oc }),
    run(" / 5.0   ", { size: 14, color: GD_TEXT_SUB }),
    run(status.toUpperCase(), { bold: true, size: 11, color: oc }),
  ], { after: 160 }));

  // ── Principle grid (2×2 table)
  children.push(capsLabel("PRINCIPLE RATINGS"));
  const ratings = data.ratings || [];
  const CW2 = [2256, 2257, 2256, 2257]; // 4 equal cols
  children.push(new Table({
    width: { size: PAGE_W, type: WidthType.DXA },
    columnWidths: CW2,
    rows: [new TableRow({ children: ratings.map((r, i) => {
      const col2 = scoreColor(r.score);
      return cell([
        para([run(r.principle, { bold: true, size: 14, color: col2 })], { after: 40 }),
        para([run(scoreStars(r.score), { size: 11, color: col2 })], { after: 40 }),
        para([run(scoreStatus(r.score), { size: 10, color: col2 })], { after: 40 }),
        para([run(`${r.violations} violation${r.violations !== 1 ? "s" : ""}`, { size: 9, color: GD_MUTED })], { after: 0 }),
      ], { bg: GD_DARK, width: CW2[i], borders: thinBorders });
    })})]
  }));
  children.push(para([], { before: 160, after: 80 }));

  // ── Rating guide
  children.push(capsLabel("RATING GUIDE"));
  const RGW = [1560, 1560, 1560, 1560, 1786];
  children.push(new Table({
    width: { size: PAGE_W, type: WidthType.DXA },
    columnWidths: RGW,
    rows: [new TableRow({ children: [
      [5, C_GREEN], [4, C_BLUE], [3, C_ORANGE], [2, C_RED_WEAK], [1, C_RED_POOR]
    ].map(([sc, col], i) =>
      cell(para([run(`${sc} — ${scoreStatus(sc)}`, { bold: true, size: 9, color: col })]),
        { bg: GD_DARK, width: RGW[i], borders: noBorders })
    )})]
  }));
  children.push(para([], { before: 160 }));
  children.push(para([run("This report is rule-based — no API key required.", { size: 10, color: GD_MUTED })], { after: 40 }));
  children.push(para([run("For AI-generated code fixes open the SOLID Review tool and click Generate Fix.", { size: 9, color: GD_MUTED })], { after: 0 }));

  // ── Page 2: Violation breakdown ──────────────────────────────────────────
  children.push(pageBreak());
  children.push(para([run("Violation Breakdown", { bold: true, size: 20, color: GD_YELLOW })],
    { after: 120, borderBottom: GD_YELLOW }));

  for (const r of ratings) {
    const col2 = scoreColor(r.score);
    const rStatus = scoreStatus(r.score);
    children.push(para([
      run(`${r.principle}`, { bold: true, size: 12, color: col2 }),
      run(`   Score: ${r.score}/5   ${rStatus}`, { size: 10, color: col2, bold: true }),
      run(`     ${r.violations} violation${r.violations !== 1 ? "s" : ""}`, { size: 9, color: GD_MUTED }),
    ], { before: 120, after: 40, borderBottom: col2 }));

    if (r.reason) {
      children.push(para([run(r.reason, { size: 11, color: GD_TEXT_SUB })], { after: 80, indent: 360 }));
    }

    // Violation bullets — grouped by title so we don't repeat "X has multiple responsibilities" 11 times
    const allViols = (data.fileResults || [])
      .flatMap(f => f.violations || [])
      .filter(v => v.principle === r.principle);
    const grouped = groupViolations(allViols).slice(0, 8);

    for (const g of grouped) {
      const sc = sevColor(g.severity);
      const titleSuffix = g.count > 1 ? `   (${g.count} files)` : "";
      children.push(para([
        run("● ", { bold: true, color: sc, size: 10 }),
        run(g.title, { bold: true, size: 10, color: GD_TEXT }),
        run(titleSuffix, { size: 9, color: GD_MUTED, bold: true }),
      ], { after: 20, indent: 360 }));

      // Show up to 6 locations, then "+ N more"
      const shown = g.locations.slice(0, 6);
      for (const loc of shown) {
        children.push(para([
          run("    ", { size: 9 }),
          run(loc.className, { bold: true, size: 10, color: GD_TEXT_SUB }),
          run("    ", { size: 9 }),
          run(`${loc.fileName}:${loc.startLine}`, { size: 9, color: GD_TEXT_SUB }),
        ], { after: 10, indent: 720 }));
      }
      if (g.locations.length > shown.length) {
        children.push(para([
          run(`+ ${g.locations.length - shown.length} more`, { size: 9, color: GD_MUTED, bold: true }),
        ], { after: 20, indent: 720 }));
      }
    }
    children.push(para([], { after: 80 }));
  }

  return children;
}

// ── FILE REPORT ───────────────────────────────────────────────────────────────
function buildFileReport(data) {
  const children = [];
  const name = data.fileName || "File";
  const date = new Date().toLocaleDateString("en-US", { year: "numeric", month: "long", day: "numeric" });
  const ratings = data.ratings || [];
  const violations = data.violations || [];
  const byPrinciple = {};
  for (const p of ["SRP","OCP","LSP","ISP"]) {
    byPrinciple[p] = violations.filter(v => v.principle === p);
  }
  const avg = ratings.length
    ? ratings.reduce((s, r) => s + r.score, 0) / ratings.length
    : 5;

  // ── Cover: title
  const gameName = data.gameName || "";
  const bundleId = data.bundleId || "";
  children.push(para([run(`SOLID Review  —  ${name}`, { bold: true, size: 22, color: GD_YELLOW })],
    { before: 0, after: 40 }));
  if (gameName) {
    children.push(para([
      run("Game: ", { size: 10, color: GD_MUTED }),
      run(gameName, { size: 10, color: GD_TEXT_SUB, bold: true }),
    ], { after: 16 }));
  }
  if (bundleId) {
    children.push(para([
      run("Bundle ID: ", { size: 10, color: GD_MUTED }),
      run(bundleId, { size: 10, color: GD_TEXT_SUB, bold: true }),
    ], { after: 16 }));
  }
  children.push(para([run(date, { size: 10, color: GD_MUTED })], { after: 200 }));

  // ── Scores at a Glance
  children.push(capsLabel("SCORES AT A GLANCE"));
  children.push(scoresAtAGlanceTable(ratings.map(r => ({
    principle: principleName(r.principle),
    score: r.score,
    reason: r.reason,
  }))));
  children.push(para([], { before: 160, after: 80 }));

  // ── Overall
  const oc = scoreColor(avg);
  const status = scoreStatus(avg);
  children.push(capsLabel("OVERALL FILE SCORE"));
  children.push(para([
    run(`${avg.toFixed(1)}`, { bold: true, size: 36, color: oc }),
    run(" / 5.0   ", { size: 14, color: GD_TEXT_SUB }),
    run(status.toUpperCase(), { bold: true, size: 11, color: oc }),
  ], { after: 160 }));

  // ── Rating guide
  const RGW = [1560, 1560, 1560, 1560, 1786];
  children.push(capsLabel("RATING GUIDE"));
  children.push(new Table({
    width: { size: PAGE_W, type: WidthType.DXA },
    columnWidths: RGW,
    rows: [new TableRow({ children: [
      [5, C_GREEN], [4, C_BLUE], [3, C_ORANGE], [2, C_RED_WEAK], [1, C_RED_POOR]
    ].map(([sc, col], i) =>
      cell(para([run(`${sc} — ${scoreStatus(sc)}`, { bold: true, size: 9, color: col })]),
        { bg: GD_DARK, width: RGW[i], borders: noBorders })
    )})]
  }));

  // ── Per-principle pages
  const PRINCIPLES = [
    { key: "SRP", full: "S — Single Responsibility", rule: "Rule: one class = one job." },
    { key: "OCP", full: "O — Open / Closed",          rule: "Rule: add new stuff without changing old code." },
    { key: "LSP", full: "L — Liskov Substitution",    rule: "Rule: subclasses should work in place of their parent." },
    { key: "ISP", full: "I — Interface Segregation",   rule: "Rule: don't force a class to depend on stuff it doesn't need." },
  ];

  for (const { key, full, rule } of PRINCIPLES) {
    children.push(pageBreak());
    const rating = ratings.find(r => r.principle === key);
    const score  = rating?.score ?? 5;
    const label  = scoreLabel(score);
    const col2   = scoreColor(score);
    const viols  = byPrinciple[key] || [];

    // Principle header
    const pStatus = scoreStatus(score);
    children.push(para([
      run(full, { bold: true, size: 18, color: col2 }),
      run(`   ${score}/5  ${pStatus}`, { size: 11, color: col2, bold: true }),
    ], { before: 0, after: 40, borderBottom: col2 }));
    children.push(para([run(rule, { size: 11, color: GD_TEXT_SUB })], { after: 160 }));

    if (viols.length === 0) {
      children.push(para([run("✓  No violations found in this file.", { bold: true, size: 12, color: col2 })],
        { after: 0 }));
      continue;
    }

    // ── THE PROBLEM
    children.push(para([run("THE PROBLEM", { bold: true, size: 10, color: GD_YELLOW })],
      { before: 80, after: 80 }));

    const grouped = groupViolations(viols);
    for (const g of grouped) {
      const sc = sevColor(g.severity);
      const titleSuffix = g.count > 1 ? `   (${g.count} occurrences)` : "";
      // Violation title row
      children.push(para([
        run(`[${g.severity}]  `, { bold: true, size: 10, color: sc }),
        run(g.title, { bold: true, size: 11, color: GD_TEXT }),
        run(titleSuffix, { size: 9, color: GD_MUTED, bold: true }),
      ], { before: 80, after: 40, indent: 0 }));

      // Description (one line — applies to all locations)
      if (g.description) {
        children.push(para([run(g.description, { size: 10, color: GD_TEXT_SUB })],
          { after: 40, indent: 360 }));
      }

      // Locations — readable size, one per line
      const shown = g.locations.slice(0, 10);
      for (const loc of shown) {
        children.push(para([
          run("• ", { color: sc, size: 10, bold: true }),
          run(loc.className, { bold: true, size: 10, color: GD_TEXT }),
          run("    ", { size: 10 }),
          run(`${loc.fileName}:${loc.startLine}`, { size: 10, color: GD_TEXT_SUB }),
        ], { after: 8, indent: 360 }));
      }
      if (g.locations.length > shown.length) {
        children.push(para([
          run(`+ ${g.locations.length - shown.length} more`, { size: 9, color: GD_MUTED, bold: true }),
        ], { after: 20, indent: 720 }));
      }

      // Evidence (if any)
      if (g.evidence) {
        children.push(para([
          run("Evidence: ", { bold: true, size: 9, color: C_ORANGE }),
          run(g.evidence, { size: 9, color: GD_TEXT_SUB }),
        ], { after: 80, indent: 360 }));
      } else {
        children.push(para([], { after: 40 }));
      }
    }

    // ── WHAT TO FIX
    children.push(para([run("WHAT TO FIX", { bold: true, size: 10, color: GD_YELLOW })],
      { before: 160, after: 40, borderBottom: GD_YELLOW }));
    children.push(para([
      run("Rule-based suggestions — no API key needed. For AI fixes open the tool and click Generate Fix.", { size: 9, color: GD_MUTED })
    ], { after: 80 }));

    // Fix table
    const FW = [3600, 5426];
    children.push(new Table({
      width: { size: PAGE_W, type: WidthType.DXA },
      columnWidths: FW,
      rows: [
        headerRow(["File", "What to do"], FW),
        ...viols.map(v => new TableRow({ children: [
          cell(para([run(v.location?.fileName || "", { size: 10, color: GD_WHITE })]),
            { width: FW[0] }),
          cell(para([run(staticGuidance(key), { size: 10, color: GD_MUTED })]),
            { width: FW[1] }),
        ]}))
      ]
    }));
  }

  // ── What to Fix First (priority page)
  if (violations.length > 0) {
    children.push(pageBreak());
    children.push(para([run("What to Fix First", { bold: true, size: 18, color: GD_YELLOW })],
      { after: 40, borderBottom: GD_YELLOW }));
    children.push(capsLabel("IN ORDER OF IMPACT"));

    const sorted = [...violations].sort((a, b) => {
      const sevOrder = { High: 2, Medium: 1, Low: 0 };
      return (sevOrder[b.severity] || 0) - (sevOrder[a.severity] || 0);
    });

    const PW = [520, 2800, 4386, 1320];
    children.push(new Table({
      width: { size: PAGE_W, type: WidthType.DXA },
      columnWidths: PW,
      rows: [
        headerRow(["#", "Task", "Why first", "Sev"], PW),
        ...sorted.map((v, i) => {
          const sc = sevColor(v.severity);
          return new TableRow({ children: [
            cell(para([run(`${i+1}`, { size: 10, color: GD_MUTED })]), { width: PW[0] }),
            cell(para([
              run(`${principleName(v.principle)}: `, { bold: true, size: 9, color: scoreColor(ratings.find(r=>r.principle===v.principle)?.score ?? 3) }),
              run(v.title || "", { size: 10, color: GD_WHITE }),
            ]), { width: PW[1] }),
            cell(para([run(whyFirst(v.principle), { size: 10, color: GD_MUTED })]), { width: PW[2] }),
            cell(para([run(v.severity || "", { bold: true, size: 9, color: sc })]), { width: PW[3] }),
          ]});
        })
      ]
    }));
  }

  return children;
}

// ── Static text helpers ───────────────────────────────────────────────────────
function staticGuidance(p) {
  const map = {
    SRP: "Pull each responsibility into its own class. Use GetComponent or SerializeField to rewire references. One class = one job.",
    OCP: "Replace if/switch on type with a shared interface or abstract base. Add new behaviour by writing a new class, not editing old code.",
    LSP: "Remove or implement any NotImplementedException. If a subclass can't honour the contract, split into a separate hierarchy.",
    ISP: "Break the large interface into smaller role-specific ones. Each class implements only the methods it actually uses.",
  };
  return map[p] || "Review and refactor so the class respects this principle.";
}
function whyFirst(p) {
  const map = {
    SRP: "God-class is the root of most bugs. Splitting it makes every other fix easier.",
    OCP: "Hardcoded logic means every feature touches working code. High regression risk.",
    LSP: "Silent substitution failures cause runtime surprises. Fix before inheriting further.",
    ISP: "No interfaces means nothing is swappable or testable. Low risk, high long-term value.",
  };
  return map[p] || "Foundational improvement — unblocks cleaner architecture.";
}
function principleName(p) {
  const map = { SRP: "S - Single Responsibility", OCP: "O - Open / Closed", LSP: "L - Liskov Substitution", ISP: "I - Interface Segregation" };
  return map[p] || p;
}
function scoreStars(score) {
  let s = "";
  for (let i = 1; i <= 5; i++) s += i <= score ? "*" : "-";
  return s;
}

// ── Entry point ───────────────────────────────────────────────────────────────
const [,, mode, outputPath, jsonPath] = process.argv;
if (!mode || !outputPath || !jsonPath) {
  console.error("Usage: solid_report.js <summary|file> <outputPath> <jsonDataPath>");
  process.exit(1);
}

const data = JSON.parse(fs.readFileSync(jsonPath, "utf8"));
const children = mode === "summary" ? buildSummary(data) : buildFileReport(data);

const doc = new Document({
  background: { color: GD_PAGE_BG },
  styles: {
    default: {
      document: { run: { font: "Calibri", size: 22, color: GD_TEXT } }
    }
  },
  sections: [{
    properties: {
      page: {
        size: { width: 11906, height: 16838 },
        margin: { top: 1080, right: 1080, bottom: 1080, left: 1080 }
      }
    },
    headers: {
      default: new Header({ children: [
        para([
          run("SOLID Review  ·  Game District", { size: 9, color: GD_YELLOW, bold: true }),
        ], { after: 0, borderBottom: GD_YELLOW })
      ]})
    },
    footers: {
      default: new Footer({ children: [
        para([
          run("GAME DISTRICT  —  SOLID REVIEW TOOL", { size: 8, color: GD_YELLOW }),
          run("     AI can make mistakes. Always review before applying.", { size: 7, color: GD_MUTED }),
        ], { before: 0, after: 0, borderBottom: undefined }),
        para([
          run("Ratings track code health for teams and self-assessment — they are not designed for evaluating individual developers.", { size: 7, color: GD_MUTED }),
        ], { before: 0, after: 0, borderBottom: undefined })
      ]})
    },
    children,
  }]
});

Packer.toBuffer(doc).then(buf => {
  fs.writeFileSync(outputPath, buf);
  console.log("OK:" + outputPath);
}).catch(err => {
  console.error(err.message);
  process.exit(1);
});