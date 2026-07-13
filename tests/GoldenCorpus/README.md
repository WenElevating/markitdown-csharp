# Multimodal Golden Corpus

This corpus is the executable deterministic baseline for the shared
PDF/DOCX/PPTX/XLSX pipeline. `manifest.json` defines required text, heading,
table-cell, asset, and diagnostic expectations plus hard quality thresholds.

The Office entries use deterministic Open XML generators in
`MarkItDown.Converters.Office.Tests`; they contain embedded PNG assets and
ChartPart caches. The PDF entries reuse checked-in fixtures. Each Office corpus
case runs through `MarkItDownEngine`, validates the normalized `DocumentModel`,
checks asset bytes, and applies the quality evaluator.

The corpus is intentionally not a substitute for a large licensed production
benchmark. Real Word text-box variants, vendor-specific chart extensions, OCR
CER calibration, and large-scale labeled recall remain separate release gates.
