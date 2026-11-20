---
name: word-professional
description: Arrange Word documents professionally - structure, real heading styles, numbering, table of contents, tables, figures with captions, headers and footers, page setup, and a final PDF check. Use when the user asks to write, format, arrange, restructure, clean up or standardize a report, procedure, proposal, letter, manual or any .docx, or to make a document "look professional".
---

# Professional Word documents

Goal: a document that is easy to navigate, consistent on every page, and still correct after someone edits it.
The way to get all three is to let styles do the formatting, never manual font changes.

## 1. Plan the structure

Decide the outline before the text. Choose the pattern that fits the document type:

| Type | Sections, in order |
|---|---|
| Report | Title page, summary, contents, background, findings, conclusion, recommendations, appendix |
| Procedure / work instruction | Purpose, scope, roles, definitions, numbered steps, records, revision history |
| Proposal | Summary, problem, proposed solution, benefit and cost, plan and schedule, risks, request |
| Letter / memo | Date, recipient, subject, purpose, details, required action, closing |
| Meeting minutes | Date and attendees, decisions, actions (owner, due date), open points |

Rules for the outline:

- At most three heading levels. A fourth level means the section should be split.
- Headings at the same level are written the same way (all noun phrases, or all actions).
- Put the summary first. Long documents (over about 5 pages) get a contents page.
- Detail that interrupts reading goes to an appendix.

## 2. Styles carry all formatting

| Content | Style | Typical setting |
|---|---|---|
| Body text | Normal | 11 pt, line spacing 1.15, 6 pt after, left-aligned |
| Chapter | Heading 1 | 16 pt bold, 18 pt before, 6 pt after, keep with next |
| Section | Heading 2 | 13 pt bold, 12 pt before, 4 pt after |
| Sub-section | Heading 3 | 11 pt bold, 8 pt before, 2 pt after |
| Document title | Title | 24-28 pt |
| Figure and table labels | Caption | 9 pt, grey or italic |
| Lists | List Bullet / List Number | hanging indent, same as body size |

Why this matters: headings built with styles produce the contents page, the navigation pane, page-break
control and PDF bookmarks automatically. Text that is only made big and bold produces none of them.

- Change a look by changing the style once, not by selecting text.
- Never use empty paragraphs for space or repeated spaces or tabs for alignment. Use paragraph spacing,
  page breaks (`Ctrl+Enter`) and tables.
- One font family for the document. Use the company template when one exists; its styles win over this table.

## 3. Page setup

| Item | Default |
|---|---|
| Paper | A4 portrait; landscape section only for wide tables |
| Margins | 2.5 cm all sides (2 cm is acceptable for dense reports) |
| Header | Document title or number on the left, revision or date on the right |
| Footer | `Page X of Y` centred or right; confidentiality mark if required |
| Title page | No header, footer or page number |

## 4. Lists, tables, figures

- **Numbered list** for steps and anything referred to later; **bullets** for items with no order. Keep list
  items parallel and short. Two levels at most.
- **Tables**: header row bold with light fill and "repeat header rows" on; numbers right-aligned, text
  left-aligned; no row broken across pages; thin grey lines, not heavy black grids.
- **Figures**: in line with text, centred, readable at printed size.
- **Captions**: `Table 1 - ...` above tables, `Figure 1 - ...` below figures, inserted as real captions so
  numbers update. Refer to them in the text by cross-reference (`see Table 2`), never "the table below".
- Every table and figure is mentioned in the text before it appears.

## 5. Automating Word (COM, no extra packages)

```powershell
$wd = New-Object -ComObject Word.Application
$wd.Visible = $false
try {
    $doc = $wd.Documents.Open($inPath, $false, $true)       # no conversion prompt, read-only
    foreach ($p in $doc.Paragraphs) {                        # audit: which style does each paragraph use?
        $name = $p.Range.ParagraphFormat.Style.NameLocal
    }
    $doc.Fields.Update() | Out-Null                          # page numbers, cross-references
    if ($doc.TablesOfContents.Count -gt 0) { $doc.TablesOfContents.Item(1).Update() }
    $doc.SaveAs2($outPath, 16)                               # 16 = .docx
    $doc.SaveAs2($pdfPath, 17)                               # 17 = PDF, for the visual check
}
finally { if ($doc) { $doc.Close($false) }; $wd.Quit() }
```

- Built-in styles have language-independent numbers: Normal `-1`, Heading 1 `-2`, Heading 2 `-3`,
  Heading 3 `-4`, Title `-63`. Use `$doc.Styles.Item(-2)` instead of the name, which changes with the
  Office language.
- Save to a new file. The original stays untouched.
- When restructuring someone's document, keep their words; change arrangement and formatting only, unless
  asked to edit the text too.

## 6. Final check

Export to PDF and look at the pages - layout problems show there, not in the text.

1. Navigation pane (or PDF bookmarks) shows a clean, complete outline.
2. Contents page is updated and page numbers match.
3. No heading alone at the bottom of a page; no table row or caption split from its table.
4. Fonts, spacing and list indents are the same on every page; no leftover manual formatting.
5. Table and figure numbers run in order and every cross-reference resolves (no `Error! Reference`).
6. Header, footer, document number, revision and date are correct.
7. Spelling and grammar pass; names, units and numbers are consistent throughout.
8. Tracked changes and comments are resolved or kept on purpose.

Report the file paths (.docx and .pdf), the page count, and anything the user must still supply.
