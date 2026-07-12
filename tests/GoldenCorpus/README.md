# Multimodal Golden Corpus

This corpus is the deterministic baseline for the shared PDF/DOCX/PPTX/XLSX pipeline.
Each entry should eventually contain the source fixture, normalized `DocumentModel`
JSON, Markdown snapshot, asset manifest, source locations, and expected diagnostics.

The initial manifest reuses the repository's checked-in fixtures so the corpus gate
can be expanded without changing the existing converter fixtures.
