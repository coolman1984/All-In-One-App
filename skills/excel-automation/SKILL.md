---
name: excel-automation
description: Automate Excel work safely and repeatably - read, clean, combine, reconcile, calculate, format and report on .xlsx/.xlsm/.csv files with PowerShell and Excel itself (COM), with a check for every total. Use when the user asks to automate, merge, compare, clean, pivot, fill, format or build a workbook, write a formula or macro, or says "do this in Excel" - even for a small one-off sheet.
---

# Excel automation

Goal: a workbook the user can trust and rerun. A result with a wrong total is worse than no result, so every
job ends with a check, and the original file is never changed.

## 1. Understand the job first

Open the file and look before writing any code. Find out:

| Question | Why it matters |
|---|---|
| Which sheet and which row hold the real headers? | Company files often have titles, merged cells or blank rows above them. |
| What is one row? (one order, one day, one part) | Decides how to join, count and total. |
| Which columns are keys, which are numbers, which are dates? | Keys stored as numbers lose leading zeros; dates stored as text do not sort. |
| Does the user need this once or every week? | Once: do it and report. Repeating: deliver a script or a formula-driven sheet. |

If the request is unclear on only one point, make the reasonable choice, state it, and continue.

## 2. Choose the method

| Situation | Method |
|---|---|
| User will keep editing the sheet by hand | Formulas and a Table, so results update by themselves |
| Many files, repeated job, large data | PowerShell script driving Excel (COM) |
| Plain .csv, no formatting needed | `Import-Csv` / `Export-Csv`, no Excel needed |
| User asks for a macro | VBA in an .xlsm, same rules as below |

On a company PC, files may be protected so that only the Office app can open them. Opening through Excel
(COM) works where reading the file bytes directly does not, and it needs no installed packages. This is the default.

## 3. Script skeleton (PowerShell + COM)

```powershell
$xl = New-Object -ComObject Excel.Application
$xl.Visible = $false; $xl.DisplayAlerts = $false; $xl.ScreenUpdating = $false
try {
    $wb = $xl.Workbooks.Open($inPath, 0, $true)          # 0 = do not update links, $true = read-only
    $ws = $wb.Worksheets.Item('Data')
    $lastRow = $ws.Cells.Item($ws.Rows.Count, 1).End(-4162).Row   # -4162 = xlUp
    $data = $ws.Range("A1:F$lastRow").Value2              # one read: 2-D array, 1-based [row, col]

    # ... work on $data in memory ...

    $out = New-Object 'object[,]' $rowCount, $colCount    # 0-based when built here
    # fill $out, then write it in one call:
    $dst = $outWb.Worksheets.Item(1)
    $dst.Range($dst.Cells.Item(1, 1), $dst.Cells.Item($rowCount, $colCount)).Value2 = $out
    $outWb.SaveAs($outPath, 51)                           # 51 = .xlsx, 52 = .xlsm, 62 = CSV UTF-8
}
finally {
    if ($wb) { $wb.Close($false) }
    $xl.Quit()
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($xl)
}
```

Rules behind the skeleton:

- **Read and write whole ranges, never cell by cell.** Each cell call crosses into Excel; 20,000 rows cell by
  cell takes minutes, one array takes under a second.
- **Always `finally` + `Quit()`.** A failed script otherwise leaves a hidden Excel holding the file locked.
- **Use `Value2`.** Dates come back as serial numbers (convert with `[DateTime]::FromOADate($n)`); currency
  is not silently rounded.
- **Never save over the input.** Write `<name>_result.xlsx` beside it. The user can compare and nothing is lost.
- **Full paths only.** Excel's working folder is not the script's folder.
- For heavy formula workbooks set `$xl.Calculation = -4135` (manual) while writing and `-4105` (automatic) after.

## 4. Clean data before using it

| Problem | Fix |
|---|---|
| Spaces around keys, mixed case | Trim; compare case-insensitively |
| Key is number in one file, text in the other | Convert both to trimmed text before matching |
| Text keys must keep leading zeros | Set the column `NumberFormat = '@'` before writing |
| Merged cells, blank cells meaning "same as above" | Fill down in memory |
| Totals or subtotal rows inside the data | Remove them, or they are counted twice |
| Duplicates | Decide with the user's meaning of one row; report how many were found |

For joins, build a hashtable on the key (`$map[$key] = $row`). It is instant, and it lets you list keys that
found no match - always report those instead of dropping them silently.

## 5. Formulas

- Prefer `XLOOKUP` and `SUMIFS` over `VLOOKUP` with a column number, which breaks when a column is inserted.
- Turn data into a Table (`$ws.ListObjects.Add(1, $range, $null, 1)`) so formulas use column names and grow
  with the data.
- Keep inputs, calculations and the report on separate sheets. Never type a number inside a formula
  (`=B2*1.14`); put the rate in a labelled cell.
- Wrap only expected gaps: `IFERROR` around everything hides real mistakes.

## 6. Make the result easy to read

- Header row: bold, filled, frozen (`$xl.ActiveWindow.SplitRow = 1; $xl.ActiveWindow.FreezePanes = $true`), filter on.
- Numbers right-aligned with one format per column (`#,##0`, `0.0%`, `yyyy-mm-dd`); text left-aligned.
- `$ws.Columns.AutoFit()` then cap very wide columns at about 50.
- One colour for headers, one for totals, red only for problems. First sheet is the answer; details follow.
- Name sheets by content (`Summary`, `Data`, `Unmatched`), not `Sheet1`.

## 7. Check before you deliver

1. **Row count**: rows in = rows out + rows removed, with the removed rows explained.
2. **Totals**: the key number column summed in the source equals the same total in the result.
3. **Spot check**: three rows, including the first and last, traced back to the source by hand.
4. **Errors**: no `#N/A`, `#REF!`, `#VALUE!` left in the result.
5. **Reopen** the saved file once to prove it opens.

If a check fails, say so with the numbers. Do not hide a difference inside a rounded figure.

## 8. Report to the user

Short and in this order: what was produced (file path), the main numbers, the checks that passed, anything
unmatched or assumed, and how to run it again.
