# Multimodal Golden Corpus

This corpus is the executable deterministic baseline for the shared
PDF/DOCX/PPTX/XLSX pipeline. `manifest.json` defines required text, heading,
table-cell, asset, and diagnostic expectations plus hard quality thresholds.

The Office entries use deterministic Open XML generators in
`MarkItDown.Converters.Office.Tests`; they contain embedded PNG assets and
ChartPart caches. The PDF entries reuse checked-in fixtures. Each Office corpus
case runs through `MarkItDownEngine`, validates the normalized `DocumentModel`,
checks asset bytes, and applies the quality evaluator.

The `realCorpus` section adds 40 redistributable fixtures from the pinned
Microsoft MarkItDown commit: 18 PDFs, 10 DOCX files, 6 PPTX files, and 6 XLSX
files. The Office and PDF tests execute every real fixture, while package checks
assert that real charts, comments, and multiple media parts are still present.
External Office relationships are permitted only in this corpus smoke test;
the normal local-only default remains covered by security tests.

The real corpus is a production-scale conversion and loss smoke gate, not a
claim that unlabeled third-party files provide full OCR CER or semantic recall
calibration. Five PDF cases now use pinned upstream expected-output labels for
text recall and reading-order checks. Word text-box variants, vendor-specific
chart extensions, OCR CER calibration, and large-scale human-reviewed recall
remain separate release gates.
