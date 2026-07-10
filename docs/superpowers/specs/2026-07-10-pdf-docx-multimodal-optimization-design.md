# PDF/DOCX Multimodal Conversion Optimization Design

**Date:** 2026-07-10

**Status:** Written-spec review

**Decision:** Local deterministic parsing with optional cloud OCR/vision fallback

**Initial scope:** PDF and DOCX

**Optimization priority:** Quality and explicit fidelity over throughput

## 1. Executive Summary

The current project has an optional vision-capable client, but PDF and DOCX
conversion do not consume it. PDF conversion can extract some images when a
filesystem asset path is supplied, while DOCX conversion ignores embedded
visual content entirely. Scanned pages, charts, equations, text boxes, and
many relationship-wrapped Word elements are either reduced to raw image links
or silently omitted.

This design replaces the direct parser-to-Markdown path for PDF and DOCX with
a bounded five-stage pipeline:

1. Normalize the source and conversion context.
2. Parse deterministic document structure into a small document model.
3. Persist and identify assets without losing provenance.
4. Enrich only content that needs OCR or visual understanding.
5. Render Markdown and a structured fidelity report.

The design remains local-first. Native text, relationships, styles, tables,
alt text, and chart data are extracted without a cloud call. Cloud analysis is
used only for scanned pages, low-confidence structures, and meaningful visual
content that lacks a deterministic description. A failed optional analysis
produces a partial result with the original asset and a diagnostic; it does
not discard content or fail the entire document.

## 2. Review Evidence

The design is based on a whole-workspace review of the current conversion
paths, tests, fixtures, and prior PDF and Office design documents.

### 2.1 Confirmed capability breaks

- `src/MarkItDown.Cli/CliRunner.cs:222` passes `LlmClient` and
  `AssetBasePath`, but `src/MarkItDown.Converters.Pdf/PdfConverter.cs:14`
  never reads `LlmClient`.
- `src/MarkItDown.Converters.Office/DocxConverter.cs:36` handles only top-level
  paragraphs and tables. Its inline renderer at line 99 handles only direct
  `Run` children, so relationship-wrapped text and non-text nodes disappear.
- `src/MarkItDown.Converters.Pdf/PdfConverter.cs:73` extracts images only when
  `AssetBasePath` is non-null. The library path overload and MCP tool do not
  create such a path.
- `src/MarkItDown.McpServer/MarkItDownTools.cs:46` calls the path-only engine
  overload. The request created at `src/MarkItDown.Core/MarkItDownEngine.cs:65`
  carries neither assets nor a vision provider.
- PDF, DOCX, PPTX, and XLSX are selected for Stream requests but then require a
  file path. The public Stream contract therefore does not match converter
  behavior.

### 2.2 Confirmed PDF quality defects

- `tests/Fixtures/scanned.pdf` contains three full-page JPEGs and no native
  letters. CLI conversion produces three image links, not searchable text.
- The layout analyzer derives page width from the widest content block rather
  than the page box. Its Y-band gap condition can merge separated regions and
  split overlapping ones.
- Table detection operates on a filtered text list, then skips a continuous
  range in the unfiltered block list. Images between table rows can be dropped.
- The current `table.pdf` output contains shifted cells and combines separate
  records into an artificial ten-column table.

### 2.3 Confirmed DOCX quality defects

- Embedded and floating images, chart parts, SmartArt, alt text, text boxes,
  and drawing relationships are not traversed.
- Hyperlink display text, fields, content controls, inserted revisions, and
  OMML equations can disappear because the renderer visits direct runs only.
- Headers, footers, footnotes, endnotes, and comments are outside the main
  body loop and are omitted.
- All numbered lists become flat unordered lists. Merged cells and nested table
  content are flattened through `InnerText`.

### 2.4 Verification baseline

- `dotnet build MarkItDown.sln --no-restore` completed with 0 errors and 10
  warnings.
- Focused PDF, Office, LLM, Media, Core, and MCP projects passed 93 tests in
  total.
- These tests do not prove multimodal fidelity. PDF image assertions can pass
  with zero images, Office fixtures contain no visual relationships, and LLM
  tests cover constructors rather than requests or responses.
- Restore reports a high-severity and a medium-severity advisory for
  `SixLabors.ImageSharp 3.1.5`. It also resolves requested `TagLibSharp 2.1.0`
  to 2.2.0.
- The full solution test command can leave CLI subprocesses running because
  `CliRunnerTests` starts `dotnet run` without a process timeout. This must be
  corrected before the full-suite gate is considered reliable.

## 3. Goals

1. Preserve every material PDF or DOCX visual asset, or report exactly why it
   could not be preserved.
2. Produce searchable Markdown from scanned PDF pages when a configured vision
   provider is available.
3. Preserve deterministic document structure before using probabilistic
   enrichment.
4. Keep image, caption, table, note, and source-position relationships intact.
5. Make CLI, Library, MCP, Stream, and nested conversion behavior consistent.
6. Bound memory, disk, provider calls, latency, and output growth.
7. Retain source compatibility for existing public constructors and common
   conversion calls.
8. Provide measurable quality gates rather than substring-only tests.

## 4. Non-Goals

- Rewriting PPTX, XLSX, HTML, media, or data converters in the first delivery.
- Supporting legacy `.doc` or `.wps` formats.
- Reproducing Word's exact pagination or pixel-perfect floating layout. DOCX
  stores logical structure and anchors, not the final renderer's page layout.
- Executing macros, OLE objects, external templates, or external relationships.
- Building a PDF rasterizer, OCR engine, or vision model from scratch.
- Making cloud analysis mandatory for documents that are fully extractable
  locally.
- Replacing the converter registry or breaking the `IConverter` signature for
  unrelated formats.

## 5. Approaches Considered

### 5.1 Patch each converter directly

Add image extraction and LLM calls inside `PdfConverter` and `DocxConverter`.

**Advantages:** Small initial diff and fast visible progress.

**Rejected because:** It duplicates asset, budget, retry, privacy, prompt, and
diagnostic logic. It also leaves entry-point inconsistency and direct
parser-to-Markdown coupling in place.

### 5.2 Separate two-stage pipelines per format

Let PDF and DOCX each parse and enrich content independently while sharing only
a vision client.

**Advantages:** Moderate change and strong format autonomy.

**Rejected because:** Provenance, assets, diagnostics, partial success, and
rendering semantics would still be implemented twice.

### 5.3 Minimal shared document model and enrichment pipeline

PDF and DOCX produce a small shared model, then use shared asset, enrichment,
rendering, and reporting services.

**Advantages:** Repairs the confirmed architectural break, supports quality
measurement, and prevents duplicated policy logic.

**Decision:** Adopt this approach. The shared model is intentionally limited to
the block and inline types needed by PDF and DOCX.

### 5.4 DOCX-to-HTML through Mammoth as the primary path

Prior local research recommended Mammoth for broad semantic DOCX conversion.

**Advantages:** Mature heading, list, link, table, and footnote conversion.

**Rejected as the primary path because:** HTML loses the relationship IDs,
source nodes, drawing anchors, raw chart data, and source-level provenance
needed to associate visual assets with enrichment and diagnostics. Mammoth may
be evaluated as a differential-test oracle, but is not part of the production
pipeline in this design.

## 6. Target Architecture

```text
CLI / MCP / Library / Nested conversion
                  |
          ConversionContext
 source + policy + limits + assets + privacy + cancellation
                  |
       PdfParser / DocxParser
                  |
           DocumentModel
 text + table + figure + equation + note + provenance
                  |
        MultimodalEnricher
 OCR + figure + chart + low-confidence table analysis
                  |
         MarkdownRenderer
                  |
   DocumentConversionResult
 Markdown + assets + diagnostics + fidelity + usage
```

The existing `IConverter.ConvertAsync` contract remains unchanged. Only the
PDF and DOCX converter implementations use the new internal pipeline in the
first delivery.

## 7. Component Responsibilities

### 7.1 DocumentSource

`DocumentSource` normalizes path and Stream input.

- A path source opens a read-only stream when parsing begins.
- A seekable caller-owned Stream is consumed without being disposed.
- A non-seekable Stream is copied to bounded temporary storage when the parser
  requires seeking.
- Filename and MIME hints remain available for registry selection and
  provenance.
- Input size is checked while spooling, not only after the copy completes.

### 7.2 ConversionContext

`ConversionContext` is created by the engine and passed through the existing
request object. It owns:

- `VisionMode`: `Off`, `Auto`, or `Required`.
- `ConversionLimits` and the remaining shared budget.
- `IAssetStore` and an asset namespace.
- `IVisionAnalyzer`, when configured.
- Privacy and diagnostic rendering options.
- Cancellation and an overall deadline.
- A conversion operation ID used for logs and MCP asset lookup.

Nested conversions derive a child context. They share the parent's remaining
budget and deadline instead of receiving a fresh unlimited context. Core
provides a `CreateChildRequest` helper so container converters propagate the
engine-owned context without making its setter public.

### 7.3 DocumentModel

The model contains ordered body blocks, document metadata, and typed document
supplements. It is not a full word processor object model.

Block types:

- `HeadingBlock`
- `ParagraphBlock`
- `ListBlock`
- `TableBlock`
- `FigureBlock`
- `EquationBlock`
- `NoteBlock`
- `PageBreakBlock`
- `DiagnosticBlock`

Inline types preserve text, emphasis, code, line breaks, hyperlinks, and note
references. Inserted revision content carries revision provenance; deleted
revision content is not added to the default model. Each block carries a
stable block ID and `SourceLocation`.

`DocumentSupplement` represents headers, footers, footnote definitions,
endnote definitions, and review comments. Footnotes and endnotes retain their
inline anchors and render after the body. Headers and footers render under
document-supplement headings after the body. They are deduplicated by section
reference type, normalized block content, and referenced asset hashes, so
distinct default, first-page, and even-page content is not collapsed. Comments
render under a review-comments heading with anchors to the referenced body
blocks.

PDF source locations contain page number, page dimensions, bounding box, and
source object identifiers when available. DOCX locations contain part URI,
logical element sequence, relationship ID, and drawing anchor information when
available.

### 7.4 AssetStore

`IAssetStore` returns an `AssetReference` after accepting bytes or a stream.
Each asset records:

- Stable ID and sanitized filename.
- SHA-256 content hash.
- MIME type and byte length.
- Source location and relationship information.
- Renderer URI.
- Whether bytes were normalized before provider upload.

Directory storage writes to a transaction directory and atomically publishes
the asset directory itself when the filesystem supports a same-volume rename.
It does not claim atomicity across a Markdown file and a separate asset
directory. Memory storage is bounded and exposes bytes through the result
asset manifest. Duplicate content reuses one stored asset but keeps all source
references.

`IAssetStore` has begin, commit, and rollback semantics. A store supplied by a
caller remains caller-owned and is not disposed by the converter; it must honor
the transaction contract. The CLI writes Markdown to a temporary file, commits
assets, then atomically replaces the Markdown file. If final replacement
fails, it performs best-effort cleanup of newly published assets. This ordering
may leave orphaned assets after a process crash, but it never publishes new
Markdown before its referenced assets. In stdout mode, assets are committed
before Markdown is written to stdout.

Image-size thresholds may control enrichment, but never asset preservation. A
small or unsupported image is recorded as decorative or undecodable rather
than silently discarded.

### 7.5 IVisionAnalyzer

The current free-form `ILlmClient` is not sufficient for document analysis.
`IVisionAnalyzer` accepts typed requests:

- `OcrPage`
- `DescribeFigure`
- `AnalyzeChart`
- `RecoverTable`

Responses are schema-validated and contain structured regions or cells,
confidence, provider/model identity, prompt/schema version, token or request
usage, and retry metadata. The first adapter reuses the existing OpenAI
configuration surface. `ILlmClient` remains supported through a compatibility
adapter during migration.

Raster tasks use `RasterVisionInput`, which references sanitized image bytes or
an asset ID. Page-capable providers may additionally advertise
`DocumentPageInput` support. That payload contains a bounded source handle and
an explicit page-number set; it never accepts an arbitrary path supplied by a
tool caller.

### 7.6 MultimodalEnricher

The enricher selects tasks from deterministic signals and the configured mode.
It never mutates trusted native text in place. Enriched content is attached to
the corresponding block with provenance and confidence.

Text, tables, equations, notes, and non-decorative visual assets are material.
A visual is decorative only when the source explicitly marks it decorative,
or when it is a repeated header/footer asset on at least three pages and covers
less than one percent of each page, or when it covers less than 0.25 percent of
the page and has no alt text, caption, relationship reference, or nearby body
reference. All other visuals are material by default. Callers may force an
asset to material but may not suppress a material diagnostic after conversion.

In `Auto` mode, the initial selection rules are:

- OCR a page when normalized native text is below 40 characters and a raster
  covers at least 60 percent of the page.
- Treat a page as mixed when it has native text plus a figure covering at
  least 20 percent of the page or a figure with a nearby caption.
- Analyze a figure when it is not marked decorative and its alt text is absent
  or non-descriptive.
- Recover a table when local table confidence is below 0.80 and the page or
  region can be represented visually.
- Analyze a DOCX chart after deterministic chart data has been extracted, so
  the provider receives both the visual and known values.

These thresholds are named options and are calibrated against the committed
corpus before becoming defaults.

### 7.7 MarkdownRenderer

The renderer consumes only the final model and an asset URI resolver.

- Simple tables use Markdown pipe syntax.
- Tables with row spans, column spans, nested blocks, or multiline cells use
  HTML table markup.
- Figures include the best available accessible description and retain their
  asset link.
- Material omissions render a visible warning block by default.
- Non-material diagnostics stay in the structured result and may be included
  as HTML comments for legacy MCP output.
- Provider-generated text is tagged in provenance but does not receive a
  special visible label unless the caller requests it.

## 8. PDF Data Flow

### 8.1 Deterministic parsing

For each page, the parser records the actual media/crop box, rotation, native
letters, words, images, and geometry. Processing is page-oriented so completed
page state can be released.

Layout analysis receives actual page dimensions. It first forms regions, then
orders blocks within regions. Full-width detection is based on page width, not
the widest content block. Region and block IDs remain stable through paragraph
merging.

Table detection occurs before paragraph merging and operates on coordinates.
It supports multiple tables and two-column tables. A detected table records the
exact source block IDs it consumes, so intervening images cannot be skipped.
Low-confidence candidates remain ordered text until visual recovery succeeds.

### 8.2 Page modality

Pages are classified as:

- `NativeText`: sufficient native text and high layout confidence.
- `Mixed`: native text plus one or more meaningful visual regions.
- `Scanned`: little native text plus a page-dominant raster.
- `ComplexLayout`: insufficient local confidence, vector-dominant content, or
  content that cannot be represented by extracted images alone.

Native pages stay local. Mixed pages preserve native text and enrich selected
figures. Scanned pages use the embedded page raster when possible; otherwise
they request an image from `IPdfPageRasterizer`. Complex pages use local blocks
plus a rendered page or provider-native document-page input.

OCR regions are merged by coordinates. Native text is retained when regions
overlap unless the native region is explicitly classified as corrupt or empty.

### 8.3 Rasterization boundary

PdfPig remains the deterministic text and geometry parser, but the installed
package does not provide page rasterization. `IPdfPageRasterizer` is therefore
an adapter boundary.

The approved design permits at most one new cross-platform rendering package.
Before it is added, the implementation plan must name the package and verify:

- Windows, Linux, and macOS x64 support for `net8.0`.
- MIT, Apache-2.0, BSD, or equivalent permissive licensing.
- No unresolved high-severity advisory.
- Active maintenance with a release in the preceding 24 months.
- Stream and page-range rendering.
- Deterministic disposal of native resources.
- Packaging that does not require an interactive desktop installation.

If no candidate passes, the production fallback is a configured cloud provider
that advertises `DocumentPageInput`. Whole-document upload is disabled by
default. It is permitted only when `AllowDocumentUpload` is explicitly true;
the provider receives the minimum page set it supports, and provenance records
that the source document was uploaded. If rasterization is unavailable and
document upload is not explicitly allowed, the page remains retained with
`PAGE_RASTERIZATION_UNAVAILABLE` and the result is partial. There is no
implicit whole-PDF upload. The project will not implement PDF rendering itself.

## 9. DOCX Data Flow

### 9.1 Logical traversal

The DOCX parser continues to use `DocumentFormat.OpenXml 3.5.1`. It walks body
children in source order and uses a recursive inline visitor for runs,
hyperlinks, fields, content controls, bookmarks, breaks, and revision nodes.

The first delivery uses an accepted revision view: inserted content is included
with revision provenance, deleted content is excluded, and insertion/deletion
counts are reported. Rendering all markup is outside the initial scope.

### 9.2 Relationships and supplemental parts

- Hyperlinks retain display text and resolved target without fetching it.
- Inline and anchored drawings resolve their image relationships and alt text.
- Chart parts expose titles, categories, series, and cached values before any
  visual analysis.
- SmartArt text is extracted from diagram data parts when available.
- Text boxes are inserted at their logical anchor position.
- Footnotes and endnotes render as Markdown note references and definitions.
- Comments render in a separate review section with anchors to referenced text.
- Headers and footers use the typed supplements and deduplication rule defined
  in Section 7.3.

External relationships, templates, and linked images are recorded but not
downloaded.

### 9.3 Lists, tables, and equations

Numbering definitions, level, start value, and style inheritance determine
ordered versus unordered lists and nesting.

Tables are first converted to a logical grid. Grid spans and vertical merges
are preserved. A simple rectangular grid renders as Markdown; a merged or
nested grid renders as HTML.

OMML equations produce an `EquationBlock` containing source OMML and extracted
linear text. Markdown math is emitted only when deterministic conversion
produces a validated representation. Otherwise the readable linear form and a
diagnostic are retained; the equation is never silently discarded.

### 9.4 Visual enrichment

Image alt text is the first description source. A descriptive alt value avoids
a cloud call. Placeholder values such as `image`, `picture`, or a filename do
not count as descriptive.

Charts combine deterministic chart data with optional visual interpretation.
Provider output may summarize known values but may not introduce numeric facts
that are absent from the extracted chart data without being marked
low-confidence.

DOCX does not expose reliable final page coordinates. The converter guarantees
logical and anchor order, not Word's rendered pagination.

## 10. Fidelity, Diagnostics, and Error Handling

### 10.1 Fidelity status

- `Complete`: every material block has deterministic content, a descriptive
  source-provided alternative, or successful required enrichment.
- `Partial`: Markdown is usable, but one or more material blocks have an
  explicit diagnostic or retained raw asset without full interpretation.
- `Failed`: the input cannot be safely opened or parsed, asset output cannot be
  committed, or a required enrichment fails.

### 10.2 Vision modes

- `Off`: deterministic parsing only.
- `Auto`: analyze selected content when a provider is available. Provider
  failure creates a partial result.
- `Required`: content selected for analysis must succeed or conversion fails.

Supplying the existing CLI LLM key selects `Auto` unless the caller explicitly
sets another mode. Without a provider, the default is `Off`.

All three modes use the same task-selection rules. In `Off`, a selected task is
not sent and any material content that lacks a deterministic description makes
the result partial with `VISION_DISABLED`. Explicit `Auto` without a provider
adds no provider diagnostic when no task is selected; otherwise it is partial
with `VISION_PROVIDER_MISSING`. `Required` without a provider likewise adds no
provider diagnostic when no task is selected and fails when at least one task
is selected. In every case, final status is still calculated from all parser,
asset, resource, and enrichment diagnostics under Section 10.1; the absence of
a selected vision task never forces `Complete`.

### 10.3 Diagnostic contract

Each diagnostic has a stable code, severity, message, source location,
recoverability, and optional exception category. Initial material codes include:

- `OCR_UNAVAILABLE`
- `VISION_TIMEOUT`
- `VISION_REJECTED`
- `VISION_RESPONSE_INVALID`
- `VISION_DISABLED`
- `VISION_PROVIDER_MISSING`
- `IMAGE_DECODE_UNSUPPORTED`
- `PAGE_RASTERIZATION_UNAVAILABLE`
- `TABLE_CONFIDENCE_LOW`
- `EQUATION_CONVERSION_UNAVAILABLE`
- `EXTERNAL_RELATIONSHIP_BLOCKED`
- `RESOURCE_LIMIT_EXCEEDED`
- `ASSET_WRITE_FAILED`

Messages may add context, but code meanings are stable public contract.

### 10.4 Failure isolation

- A vision failure affects one task, not the entire document in `Auto` mode.
- A locally undecodable image remains in the asset manifest with a diagnostic.
- A single page or supplemental DOCX part may produce a partial result if the
  remaining model is safe and coherent.
- A corrupt package root, invalid PDF cross-reference structure, or failed
  asset commit is fatal.
- Cancellation is never wrapped as `ConversionException`.

Provider retries apply only to `408`, `429`, and `5xx`, at most twice with
backoff and jitter. Other `4xx` responses are not retried.

## 11. Resource and Privacy Policy

### 11.1 Default limits

| Limit | Default |
|---|---:|
| Input bytes | 256 MiB |
| PDF pages | 500 |
| Asset count | 1,000 |
| Single asset bytes | 32 MiB |
| Total asset bytes | 512 MiB |
| Decoded pixels per image | 50 MP |
| Vision tasks per document | 100 |
| Concurrent vision tasks | 2 |
| Vision timeout per task | 90 seconds |
| Overall conversion deadline | 15 minutes |
| Markdown characters | 20,000,000 |

Library callers may configure limits. CLI and MCP always start with finite
defaults. Limit checks occur before allocation or provider submission whenever
the size is knowable. The overall deadline starts at the engine entry and
includes source spooling, parsing, all retry attempts, rendering, and asset
commit. The 90-second vision-task timeout covers all attempts for one task, not
each retry independently, and is always capped by the remaining overall
deadline.

### 11.2 Privacy controls

- Upload only the page or crop required by the selected task by default.
- Uploading a source PDF is prohibited unless `AllowDocumentUpload` is true;
  provider page-range limitations and the uploaded page set are recorded.
- Re-encode provider-bound raster images and remove EXIF, GPS, and unrelated
  metadata.
- Never fetch DOCX external relationships by default.
- Never write API keys, source image bytes, or full provider response bodies to
  normal logs.
- Cache tasks by sanitized input hash, task type, provider/model, and schema
  version.
- Keep provider cache disabled by default for MCP unless an administrator
  configures an encrypted or access-controlled cache.

## 12. Public Contracts and Entry Points

### 12.1 Source-compatible model changes

`DocumentConversionRequest` retains existing properties and directly adds
optional `Options`, `AssetStore`, and `VisionAnalyzer` properties. It also
exposes an engine-populated `Context` getter whose initializer is internal to
Core. `AssetBasePath` adapts to a directory asset store when `AssetStore` is
null. Providing both `AssetBasePath` and `AssetStore` is invalid and throws
`ArgumentException`. Providing both `VisionAnalyzer` and legacy `LlmClient` is
also invalid. When only `LlmClient` is present, Core installs the compatibility
vision adapter.

`DocumentConversionResult` retains its current positional constructor and
properties. Additive init-only properties expose:

- `Assets`
- `Diagnostics`
- `FidelityStatus`
- `Usage`

`ILlmClient` remains available for compatibility. New PDF and DOCX features
use `IVisionAnalyzer` internally.

### 12.2 Asset delivery

| Entry | Asset behavior |
|---|---|
| CLI | Directory store at `{output-stem}_files`; relative Markdown URIs |
| Library with explicit directory | Directory store selected by caller |
| Library without directory | Bounded memory store; `asset://` URIs and bytes in `Assets` |
| MCP | Bounded temporary store; opaque `markitdown://` resource URIs |
| Nested conversion | Child namespace sharing parent budget and lifetime |

The path and Stream convenience overloads use the same finite default options.
Their default asset store is bounded memory storage; they no longer disable
asset extraction merely because a directory was not supplied.

### 12.3 CLI

Existing LLM options remain. Add:

- `--vision off|auto|required`
- `--fail-on-partial`
- `--diagnostics <path>`
- `--allow-document-upload`
- `--pipeline legacy|multimodal` during the staged migration window

A partial result returns exit code 0 by default for script compatibility and
writes warnings to stderr. `--fail-on-partial` returns exit code 3 for a
partial result. The temporary pipeline option is removed after the migration
window described in Phase 6.

`--allow-document-upload` is false by default and is valid only with `Auto` or
`Required`; it is the CLI authorization for provider-native PDF input. Library
callers use `ConversionOptions.Privacy.AllowDocumentUpload`, also false by
default.

The same pipeline choice is exposed to Library callers as
`ConversionOptions.PipelineMode`. During the preview release, `legacy` is the
default for the existing CLI command, library overloads, and legacy MCP tool;
the detailed MCP tool always uses `multimodal`. Because this is public API, it
remains supported until the next major version.

During preview, explicitly using `--vision`, `--allow-document-upload`,
`--fail-on-partial`, or `--diagnostics` requires
`--pipeline multimodal`. Combining one of those options with explicit or
default `legacy` is a CLI usage error; it never silently switches pipelines or
ignores the option. Supplying only the existing `--llm-key` preserves legacy
behavior, including the fact that legacy PDF and DOCX conversion do not consume
document vision. Supplying `--llm-key --pipeline multimodal` creates the vision
adapter and selects `Auto` unless `--vision` says otherwise.

Library callers follow the same rule. Setting `VisionAnalyzer`, a non-default
`VisionMode`, `Privacy.AllowDocumentUpload`, or the new `AssetStore` while
`PipelineMode` is `Legacy` throws `ArgumentException` during context creation.
The existing `LlmClient` and `AssetBasePath` properties remain valid for legacy
compatibility.

### 12.4 MCP

The current Markdown-only `convert_to_markdown` tool remains for compatibility.
During the preview release it follows the server's `PipelineMode`, whose
default is `legacy`. When configured for `multimodal`, it uses the shared
pipeline, adds material diagnostic summaries as HTML comments, and renders
assets with the resource URI described below.

A new `convert_to_markdown_detailed` tool returns Markdown, fidelity status,
diagnostics, usage, and asset resource URIs. It accepts
`allow_document_upload`, false by default, as the only MCP authorization for
provider-native PDF input; the legacy tool never permits whole-document
upload.

Assets are exposed through the MCP resource template
`markitdown://conversion/{conversionId}/assets/{assetId}`. The same URI is used
inside multimodal Markdown. Temporary conversions expire 15 minutes after the
successful result is published and remain subject to the shared asset byte
limit; an administrator may configure a value from 1 to 60 minutes.
Conversion IDs contain at least 256 bits of cryptographic randomness and are
scoped to the server process and, when the transport exposes one, the
authenticated principal. The resource handler accepts no path, resolves assets
only inside the matching conversion, and returns `CONVERSION_EXPIRED` after
expiry. Lazy cleanup runs on resource access and a background cleanup pass runs
once per minute.

## 13. Observability

Structured events use the operation ID and include:

- Parser, enrichment, and rendering durations.
- Page and block counts.
- Assets retained, deduplicated, skipped, or undecodable.
- Vision task type, provider request ID, attempts, latency, and usage.
- Cache hits and misses.
- Limit and diagnostic codes.
- Final fidelity status.

No event contains raw document text or image bytes by default.

## 14. Test Strategy

### 14.1 Corpus

The committed corpus combines anonymized real files and generated adversarial
files. The initial gate contains at least 24 PDFs and 16 DOCX files. PDF files
are split evenly across native, scanned, mixed, and complex/table-heavy groups.
DOCX files are split evenly across text/relationships, images/charts,
lists/tables, and notes/revisions groups. At least half of each format's corpus
comes from independently authored or anonymized real-world documents rather
than the converter's own fixture generator.

PDF coverage:

- Native single-column, multicolumn, spanning headings, and rotated pages.
- Clean scans, degraded scans, Chinese/English scans, and mixed pages.
- Two-column tables, multiple tables, merged cells, and low-confidence tables.
- Images, captions, charts, equations, vector-dominant content, and unsupported
  image encodings.

DOCX coverage:

- Inline and floating images, alt text, charts, SmartArt, and text boxes.
- Hyperlinks, fields, content controls, tracked revisions, and OMML.
- Nested lists, merged tables, headers, footers, notes, and comments.

Each fixture stores normalized `DocumentModel` golden JSON, Markdown snapshots,
asset expectations, source locations, and expected diagnostics.

### 14.2 Deterministic tests

- Unit tests cover parsers, block ordering, table grids, asset transactions,
  limits, and diagnostic mapping.
- Contract tests use a fake HTTP/provider layer for requests, structured
  responses, timeouts, retries, refusal, and invalid output.
- Integration tests compare path and Stream input and normalize entry-specific
  asset URIs before comparing CLI, Library, and MCP results.
- Resource tests cover cancellation, non-seekable streams, oversized inputs,
  decompression pressure, high-pixel images, asset limits, and path boundaries.
- CLI process tests receive a hard timeout and terminate their process tree.

Live provider tests run manually or on a scheduled workflow. They do not gate
ordinary pull requests because remote model output and service availability are
not deterministic.

Deterministic preservation, asset recall, DOCX element preservation, native
reading order, limits, and diagnostics run in CI with `VisionMode=Off` or a fake
provider as appropriate. OCR CER, visually recovered table F1, and final mixed
page ordering are release-qualification gates in `VisionMode=Auto` using a
pinned provider, model, prompt version, and schema version. Each live quality
case runs three times; the median score must pass the threshold, and no run may
silently omit a labeled material block. Changing the pinned provider or prompt
requires recording a new baseline before release qualification.

The CLI/Library/MCP consistency gate explicitly configures
`PipelineMode=Multimodal` for every entry. Legacy output is covered only by
backward-compatibility regression tests and is not mixed into new-pipeline
quality metrics.

### 14.3 Acceptance gates

Asset recall counts a labeled asset as preserved when the result contains its
bytes or a resolvable asset reference with the expected source relationship.
DOCX element preservation requires a correct semantic block, an original raw
element representation, or a resolvable asset linked to the expected source;
a diagnostic alone does not count as preservation. Failure diagnostics are
measured separately. Table cell F1 compares normalized cell text, row, column,
and spans. Reading-order accuracy is pairwise block order accuracy over labeled
non-decorative blocks.

| Metric | Required gate |
|---|---:|
| Labeled visual asset recall | 100% |
| Labeled DOCX element preservation | 100% |
| Clean scan OCR character error rate | <= 3% |
| Degraded scan OCR character error rate | <= 8% |
| Table cell F1 | >= 95% |
| Labeled reading-order accuracy | >= 95% |
| Forced material-failure diagnostic coverage | 100% |
| Unnecessary vision calls on native text pages | 0 |
| Normalized CLI/Library/MCP result consistency | 100% |
| Unresolved high-severity dependency advisories | 0 |

Native-text PDF and ordinary DOCX conversion without provider calls must stay
within 1.5 times the established p95 baseline on the committed corpus. Cloud
paths are governed by deadlines and budgets rather than a fixed end-to-end
latency gate.

## 15. Delivery Sequence

### Phase 0: Baseline and red tests

- Commit the labeled corpus, golden model format, and scoring utilities.
- Reproduce every confirmed silent-loss and ordering defect with failing tests.
- Add a bounded CLI subprocess test harness.

### Phase 1: Shared contracts

- Add source normalization, context, limits, assets, diagnostics, fidelity, and
  additive result properties.
- Preserve legacy converter behavior while shared infrastructure is introduced.

### Phase 2: PDF deterministic pipeline

- Implement path/Stream parity, page model, correct page geometry, region-first
  reading order, coordinate table detection, and complete asset manifests.
- Add page modality classification and the rasterization adapter boundary.

### Phase 3: DOCX deterministic pipeline

- Implement recursive inline traversal, relationship-aware assets, numbering,
  tables, supplemental parts, chart data, and equation preservation.

### Phase 4: Multimodal enrichment

- Add the typed vision provider, provider-bound image normalization, task
  selection, caching, retry, usage, and partial-result behavior.
- Add the approved rasterizer package only if it passes the dependency gate.

### Phase 5: Entry integration

- Apply one context policy across CLI, Library, MCP, and nested conversions.
- Add detailed CLI diagnostics and MCP asset access.

### Phase 6: Quality and rollout

- Run corpus, resource, security, and performance gates.
- Preview release: keep `legacy` as the default for existing entry points and
  allow explicit `multimodal`; the detailed MCP tool uses `multimodal`.
- Default transition: after all acceptance gates pass and one preview release
  shows no unresolved critical regression, make `multimodal` the default while
  retaining explicit `legacy` for one minor release.
- Stabilization minor: retain explicit `legacy` while `multimodal` is the
  default and record migration evidence.
- Cleanup major: remove `PipelineMode`, `--pipeline`, and the duplicate legacy
  PDF/DOCX path only in the next major release.

## 16. Risks and Mitigations

| Risk | Mitigation |
|---|---|
| Provider output is nondeterministic | Typed schemas, confidence, fake-provider CI, live tests outside PR gate |
| PDF rasterizer adds native deployment risk | One-package limit and explicit cross-platform/license/security gate |
| DOCX direct parsing grows complex | Small IR, focused visitors, real golden corpus, no pixel-layout promise |
| Cloud usage leaks data or costs too much | Minimum-scope uploads, explicit document consent, metadata stripping, finite tasks, caching |
| Rich result breaks callers | Additive properties, retained constructors, legacy CLI/MCP behavior |
| Quality pipeline slows native documents | Local-first task selection and p95 no-cloud performance gate |
| Partial output is mistaken for complete | Fidelity status, material diagnostic placeholders, `--fail-on-partial` |
| Existing vulnerable image package enters new paths | Resolve high advisories before release and fail the dependency gate otherwise |

## 17. Completion Criteria

The optimization is complete only when:

1. All acceptance metrics pass on the committed corpus.
2. PDF and DOCX path and Stream inputs are equivalent after URI normalization.
3. No labeled material asset or DOCX element disappears without a diagnostic.
4. CLI, Library, MCP, and nested conversions share limits and vision policy.
5. Auto-mode provider failure returns coherent partial output and retained
   assets.
6. Required-mode provider failure is explicit and deterministic.
7. Full solution build, test, formatting, and static analysis gates pass.
8. CLI test processes cannot outlive their configured timeout.
9. No unresolved high-severity dependency advisory remains.
10. Public migration notes document the new result fields, CLI options, MCP
    detailed tool, and temporary legacy selection.
