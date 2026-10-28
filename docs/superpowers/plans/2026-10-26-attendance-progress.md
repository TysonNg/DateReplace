# Implementation ledger — partial dates, popup backgrounds, attendance calendar

Approved specification: user implementation request, 2026-10-26.
Workspace: D:/Projects/DateReplace. No Git metadata exists; preserve files in place, no worktree/commits possible.

- Partial date detector, formatting and review label: implemented by Bernoulli; targeted 35 tests green.
- Attendance service: analyze/preview/export; synthetic template 6 tests red then green.
- Attendance ViewModel and screen: 5 tests red then green; connected to Tools and back navigation.
- Popup resources: independent implementation by Meitner, awaiting final report.
- Remaining: real workbook smoke, independent code review, live UI, full suite and packaged launcher.

Ruling: generated short-month files retain 31 day columns with blank excess headers and remain recognizable on subsequent runs.
Ruling: hash source during preview and check again under a write-excluding lock during export; require a new preview if changed.
Ruling: fail export when backup fails; remove the failed operation backup record if export fails.
Ruling: preserve original package parts; allow only selected worksheet and appended fill/style records. Reject new OpenXML validation errors relative to original template.

Final verification update 2026-10-27:
- Review findings reproduced then fixed: daily-total extra-day formulas, unrelated chartsheets, row-specific old Sunday fills, explicitly green target fills.
- Real Excel namespace reordering reproduced with a default-namespace fixture; semantic XML comparison passes without dropping namespace-qualified names.
- Attendance service targeted tests: 11/11 green.
- Full suite before layout extension: Core 204 + Word 33 + App 78 = 315/315 green.
- Theme test extended with offline WPF 100%/150% layout rendering; both pass with back/export actions in bounds.
- Real user workbook: November blocked at Sheet1!AH17; December export passed, source hash unchanged. Independent read-only check verifies dates/weekdays/fills, attendance, font/borders/alignment, merged cells and print area.
- User pressed Escape to stop native Computer Use; no further desktop automation performed. Live UI click-through remains unverified, distinct from offline resource/layout checks.

Compact attendance fix — 2026-10-27:
- User's two NSG workbooks use30day columns, Total label Tổng cộng, no grand-total row,20 workers and shared totals formulas.
- Analyze accepts28–31days, optional grand total, explicit worker boundaries; refuses interrupted identities rather than truncating the table.
- Compact export reserves31 day columns by table-local expansion; moves total/notes and header merges, splits widths, preserves source attendance and footer positions. Shared formula master ranges are remapped as groups.
- Preview blocks collisions, cross-boundary merges/shared formulas, outside formulas/named ranges referencing moved totals, and arrays/data-table formulas.
- Targeted tests:22/22; full suite215 Core +33 Word +78 App =326 passed.
- Both actual NSG workbooks recognized/exported October2026/re-recognized with unchanged source hashes. Independent read-only workbook checks verified all20 workers,30 original day values, blank31st day, formulas/notes, Sunday fills, fonts/borders, footer cells, widths and print area.

Period header extension — 2026-10-27:
- Update month/year plus labelled From/To or dash date ranges in the selected table's immediate header, preserving labels and digit widths. Support split text fields and neighboring label/date text cells; protect formulas.
- Recognize Tháng MM năm YYYY as well as MM/YYYY. Determine header left edge from STT so fields left of the month cell remain included.
- Read planned period edits from the read-only source before output editing to preserve sharedStrings bytes unchanged.
- Ten new period tests passed; all calendar-related32/32. Full suite225 Core +33 Word +78 App =336/336 green.
- Both actual NSG workbooks tested via read snapshots (one original open in Excel): selected October2026 produced Thời gian: tháng10/2026 (từ01/10/2026 đến31/10/2026). Original files were only read.

Excel recovery warning fix — 2026-10-28:
- Confirmed actual Excel recovery log error214800_01.xml: removed Formula records from /xl/calcChain.xml. Source chain still referenced AH totals after output moved formulas to AI.
- New AttendanceCalendarCalculation partial resets cached chain through SDK, removing its relationship/content type, and sets FullCalculationOnLoad/ForceFullCalculation while preserving manual/auto mode and other settings.
- Independent verification allows only the old chain removal, the matching package relationship/content-type removal and the two calculation flags. XML comparisons normalize namespace declarations/attribute order and read byte-stream XML with BOM detection.
- Three regression cases (compact, fixed31days, no prior chain) pass; metadata rewritten with nativeExcel attribute order included. All35 targeted attendance tests pass.
- Real Excel normal read-only opening of old bad copy failed. New output opened normally via isolated ExcelCOM instance; header correct, AI15 formula SUM(D15:AH15),20 employee totals sum600. Closed without saving.
- Saved corrected new workbook Chấm công tháng9_10_2026_da_sua.xlsx in user's original folder. Existing source/bad output not overwritten.
- Previous turn's final comparison edit was prevented by an automatic approval-review usage-limit failure, not a safety rejection. User resumed; normal approval review succeeded on retry.
- Final full verification: Core228 +Word33 +App78 =339/339 passed. Word integration startup took longer but completed; no test failures.
- Release build Publish_App_Calendar_v4 prepared successfully; launcher switched only after full-suite success.
