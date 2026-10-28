# Word and Excel workspace design

Approved scope: user accepted the proposed .xlsx text/numeric date support and UI/UX redesign including clear section borders.

Keep WPF/.NET 8 and existing Word pipeline. Add .xlsx using the existing OpenXML dependency. Native date cells require a date number format; general numbers are not dates. Preserve formulas and file originals, styles, time fractions and 1904 date system. Scan shared/inline text and cached formula dates; formulas remain unselectable. Include hidden-sheet option, sheet filters and spreadsheet cell context.

Use five visible sections: app header/tools, progress strip, filters/bulk actions, split list/details workspace, footer readiness/actions. Border #CBD5E1 at 1px, white panels, #F1F5F9 canvas, 16px gaps, 8px radii. Compact review table with adjustable context panel; dd/MM/yyyy. Dashboard file selection drives detected date and sheet summary, empty-state three steps. Excel context is a nearby-cell grid.

No .xls binary support, OCR engine replacement or custom holiday calendar in this scope. Existing Word behavior and tests must remain. Never stop running user app. Publish in separate Publish_App_Excel folder and add a separate launcher.
