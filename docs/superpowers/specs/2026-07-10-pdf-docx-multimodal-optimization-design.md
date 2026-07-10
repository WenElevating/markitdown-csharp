# PDF/DOCX 多模态转换优化设计

**日期：** 2026-07-10

**状态：** 等待书面设计复核

**决策：** 本地确定性解析为主，可选云端 OCR/视觉兜底

**首期范围：** PDF 和 DOCX

**优化取向：** 质量与显式保真度优先于吞吐量

## 1. 摘要

当前项目已经有可选的视觉模型客户端，但 PDF 和 DOCX 转换链路并未真正消费这项能力。PDF 仅在调用方提供文件系统资产目录时提取部分图片；DOCX 则完全忽略文档中的视觉内容。扫描页、图表、公式、文本框，以及大量被关系节点包裹的 Word 内容，要么只剩一个原始图片链接，要么被静默丢弃。

本设计将 PDF 和 DOCX 从当前的“解析器直接输出 Markdown”改为有明确边界的五阶段管线：

1. 规范化输入源和转换上下文。
2. 将可确定解析的文档结构转换为精简的文档模型。
3. 保存并标识资产，同时保留来源信息。
4. 仅对确实需要 OCR 或视觉理解的内容进行增强。
5. 统一渲染 Markdown 和结构化保真报告。

整个方案坚持本地优先。原生文字、关系、样式、表格、替代文本和图表数据优先在本地提取。只有扫描页、低置信度结构，以及缺少可靠确定性描述的重要视觉内容才会进入云端分析。

在可选分析失败时，系统返回带原始资产和明确诊断的部分成功结果，不会丢弃内容，也不会让整份文档无条件失败。

### 1.1 术语约定

本文正文使用中文；代码类型、枚举值、命令参数和协议字段保留原名：

- 实质内容：影响文档语义、搜索或理解的内容。
- 装饰性内容：可以安全省略语义解释的纯装饰资产。
- 诊断：描述缺失、降级、超限或失败原因的结构化记录。
- 来源追踪：内容来自哪一页、节点、关系或视觉任务的证据。
- 视觉提供者：执行 OCR、图片、图表或表格分析的外部服务。
- 测试样例：纳入自动化测试的输入文档。
- 评测语料：用于整体质量度量的一组标注文档。
- 基线：改动前或固定模型版本下记录的质量与性能结果。

## 2. 审查证据

本设计来自对当前工作区转换链路、测试样例，以及既有 `PDF/Office` 设计文档的完整审查。

### 2.1 已确认的能力断点

- `src/MarkItDown.Cli/CliRunner.cs:222` 已传入 `LlmClient` 和 `AssetBasePath`，但 `src/MarkItDown.Converters.Pdf/PdfConverter.cs:14` 从未读取 `LlmClient`。
- `src/MarkItDown.Converters.Office/DocxConverter.cs:36` 只处理顶层段落和表格；第 99 行开始的行内渲染器只处理直属 `Run`，因此关系节点包裹的文字和非文字节点会消失。
- `src/MarkItDown.Converters.Pdf/PdfConverter.cs:73` 仅在 `AssetBasePath` 非空时提取图片。库 API 的路径快捷重载和 MCP 工具都不会创建该路径。
- `src/MarkItDown.McpServer/MarkItDownTools.cs:46` 调用路径快捷重载；`src/MarkItDown.Core/MarkItDownEngine.cs:65` 创建的请求不带资产存储或视觉提供者。
- PDF、DOCX、PPTX 和 XLSX 能被 `Stream` 请求正确选中，但各转换器随后要求 `FilePath`，公开的 `Stream` 契约与实际实现不一致。首期只修复 PDF 和 DOCX；PPTX/XLSX 作为已知遗留问题保留。

### 2.2 已确认的 PDF 质量问题

- `tests/Fixtures/scanned.pdf` 包含三张整页 JPEG，没有原生字符。当前 CLI 只输出三个图片链接，不产生可搜索文字。
- 布局分析器使用“最宽内容块”推断页面宽度，而不使用真实页面框；Y 轴分带的间距条件还可能合并本应分开的区域、拆开本应重叠的区域。
- 表格检测先过滤出纯文字列表，渲染时却在原始内容块列表中跳过连续区间，因此表格行之间的图片可能被吞掉。
- 当前 `table.pdf` 输出已经出现单元格错位，并把不同记录拼成一个伪造的十列表格。

### 2.3 已确认的 DOCX 质量问题

- 内嵌图片、浮动图片、`ChartPart`、SmartArt、替代文本、文本框和绘图关系都没有被遍历。
- 超链接显示文字、字段、内容控件、插入修订和 OMML 公式可能因为只遍历直属 `Run` 而消失。
- 页眉、页脚、脚注、尾注和批注位于主文档正文循环之外，当前全部被省略。
- 所有编号列表都会被降为扁平无序列表；合并单元格和嵌套表格内容通过 `InnerText` 被压平。

### 2.4 当前验证基线

- `dotnet build MarkItDown.sln --no-restore` 成功，0 个错误、10 个警告。
- `PDF`、`Office`、`LLM`、`Media`、`Core` 和 `MCP` 六个相关测试项目共 93 项测试通过。
- 这些测试不能证明多模态保真。PDF 图片断言在零图片时也可能通过；Office 测试样例不包含视觉关系；LLM 测试只覆盖构造函数，没有覆盖请求和响应。
- 依赖恢复报告显示 `SixLabors.ImageSharp 3.1.5` 存在一个高危和一个中危公告；请求的 `TagLibSharp 2.1.0` 实际解析为 2.2.0。
- 全量解决方案测试可能留下 CLI 子进程，因为 `CliRunnerTests` 启动 `dotnet run` 时没有进程超时。修复该问题之前，不能把全量测试门视为可靠。

## 3. 目标

1. 保留所有有实质意义的 PDF/DOCX 视觉资产，或者准确报告未能保留的原因。
2. 在配置视觉提供者后，为扫描 PDF 生成可搜索 Markdown。
3. 在使用概率性增强之前，完整保留可确定解析的文档结构。
4. 保持图片、图注、表格、注释和来源位置之间的关系。
5. 统一 PDF/DOCX 在 CLI、库 API、MCP、`Stream` 和嵌套转换中的行为。
6. 限制内存、磁盘、提供者调用、延迟和输出增长。
7. 保持现有公共构造函数和常用转换调用的源码兼容性。
8. 用可量化的质量门替代仅检查子字符串的测试。

## 4. 非目标

- 首期不重写 PPTX、XLSX、HTML、媒体或数据转换器。
- 首期不修复 PPTX/XLSX 的 `Stream` 契约；共享输入基础设施会为后续修复提供边界，但不会改变它们的行为。
- 不支持旧版 `.doc` 或 `.wps`。
- 不还原 Word 的精确分页或像素级浮动布局。DOCX 保存的是逻辑结构和锚点，不是最终排版器的页面结果。
- 不执行宏、OLE 对象、外部模板或外部关系。
- 不自行实现 PDF 页面栅格化器、OCR 引擎或视觉模型。
- 对本地可完整提取的文档，不强制调用云端分析。
- 不替换 `ConverterRegistry`，也不为无关格式破坏 `IConverter` 签名。

## 5. 方案比较

### 5.1 直接修补各转换器

在 `PdfConverter` 和 `DocxConverter` 内直接增加图片提取和视觉模型调用。

**优点：** 初始改动较小，能很快看到结果。

**不采用原因：** 会重复资产、预算、重试、隐私、提示词和诊断逻辑；入口不一致以及“解析器直接输出 Markdown”的耦合仍然存在。

### 5.2 每种格式独立的两阶段管线

PDF 和 DOCX 各自完成解析与增强，只共享视觉客户端。

**优点：** 改动适中，格式自主性较强。

**不采用原因：** 来源追踪、资产、诊断、部分成功和渲染语义仍要实现两遍。

### 5.3 最小共享文档模型与增强管线

PDF 和 DOCX 产生小型共享模型，再复用资产、增强、渲染和报告服务。

**优点：** 能修复已确认的架构断点，支持质量度量，并消除策略逻辑重复。

**决策：** 采用该方案。共享模型只包含 PDF 和 DOCX 首期需要的内容块与行内类型，不提前为其他格式做抽象。

### 5.4 以 Mammoth DOCX 转 HTML 作为主路径

既有本地调研曾推荐 Mammoth 处理 DOCX 的通用语义转换。

**优点：** 标题、列表、链接、表格和脚注转换较成熟。

**不采用为主路径的原因：** HTML 会丢失视觉增强和诊断所需的关系 ID、源节点、绘图锚点、原始图表数据及来源追踪信息。Mammoth 可以作为差异测试的参照实现，但不进入本设计的生产管线。

## 6. 目标架构

~~~text
CLI / MCP / 库 API / 嵌套转换
                 |
         ConversionContext
输入源 + 策略 + 限制 + 资产 + 隐私 + 取消
                 |
       PdfParser / DocxParser
                 |
          DocumentModel
文字 + 表格 + 图片 + 公式 + 注释 + 来源
                 |
       MultimodalEnricher
OCR + 图片描述 + 图表分析 + 低置信度表格恢复
                 |
        MarkdownRenderer
                 |
   DocumentConversionResult
Markdown + 资产 + 诊断 + 保真状态 + 用量
~~~

现有 `IConverter.ConvertAsync` 契约保持不变。首期只有 PDF 和 DOCX 转换器内部使用新管线。

## 7. 组件职责

### 7.1 `DocumentSource`

`DocumentSource` 统一路径输入和 `Stream` 输入。

- 路径输入在开始解析时打开只读流。
- 调用方拥有、支持随机定位的 `Stream` 会被消费，但不会被转换器释放。
- 解析器要求随机定位而输入不支持时，将内容复制到受限临时存储。
- 文件名和 MIME 提示继续用于转换器选择和来源追踪。
- 暂存复制期间持续检查输入大小，不能等复制完成后才判断超限。

### 7.2 `ConversionContext`

`ConversionContext` 由 `Engine` 创建，通过现有请求对象传递，包含：

- 已解析且非空的 `VisionMode`：`Off`、`Auto` 或 `Required`。
- `ConversionLimits` 和剩余共享预算。
- `IAssetStore` 与资产命名空间。
- 可选的 `IVisionAnalyzer`。
- 隐私设置和诊断渲染选项。
- `CancellationToken` 与总时限。
- 用于日志关联的操作 ID。该 ID 不是 MCP 资产访问凭证。

嵌套转换通过派生子上下文继承父级剩余预算和总时限，不能获得一套新的无限预算。`Core` 提供 `CreateChildRequest` 辅助方法，使容器转换器能够传播由 `Engine` 管理的上下文，而无需公开其设置方法。

### 7.3 `DocumentModel`

模型包含按顺序排列的正文内容块、文档元数据和带类型的补充内容。它不是完整的文字处理器对象模型。

内容块类型：

- `HeadingBlock`
- `ParagraphBlock`
- `ListBlock`
- `TableBlock`
- `FigureBlock`
- `EquationBlock`
- `NoteBlock`
- `PageBreakBlock`
- `DiagnosticBlock`

行内类型保留文字、强调、代码、换行、超链接和注释引用。插入修订内容携带修订来源追踪；删除修订不进入默认模型。每个内容块都有稳定的内容块 ID 和 `SourceLocation`。

`DocumentSupplement` 表示页眉、页脚、脚注定义、尾注定义和审阅批注：

- 脚注和尾注保留行内锚点，在正文后输出定义。
- 页眉和页脚在正文后的补充内容标题下输出。
- 去重键由节引用类型、规范化内容块和引用资产哈希组成，不能把默认页、首页和偶数页内容误合并。
- 批注在“审阅批注”标题下输出，并保留到对应正文内容块的锚点。

PDF 的来源位置包含页码、页面尺寸、边界框，以及可用的源对象 ID。DOCX 的来源位置包含文档部件 URI、逻辑元素序号、关系 ID 和可用的绘图锚点信息。

### 7.4 `AssetStore`

`IAssetStore` 接收字节或流，返回 `AssetReference`。每项资产记录：

- 稳定 ID 和经过清理的文件名。
- SHA-256 内容哈希。
- MIME 类型和字节长度。
- 来源位置和关系信息。
- 渲染器 URI。
- 上传给视觉提供者前是否已规范化。

目录存储先写事务目录，并在文件系统支持同卷重命名时原子发布不可变的资产版本目录。它不承诺 Markdown 文件与独立资产目录之间的跨路径原子性。

`IAssetStore` 提供 `Begin`、`Commit` 和 `Rollback` 语义。每次顶层转换只有根 `Engine` 可以开始、提交或回滚事务；嵌套转换只能在派生命名空间写入同一事务，不能自行改变事务状态。调用方传入的存储仍由调用方拥有，转换器不负责释放，但根 `Engine` 仍拥有本次转换的事务边界；该存储必须遵守事务契约。

CLI 的提交顺序：

1. CLI 用规范化完整输出身份的 SHA-256 哈希生成稳定 `ownerId`，并按该 `ownerId` 获取跨进程独占发布锁；标准输出模式使用规范化输入身份和资产根目录共同生成所有者。锁等待受整体转换时限约束，超时产生 `OUTPUT_WRITE_FAILED`。
2. 持锁期间，根 `Engine` 渲染 Markdown，并把资产原子提交到 `{output-stem}_files/{ownerId}/{publicationId}` 这一不可变的新版本目录。
3. CLI 把 Markdown 写入最终 Markdown 所在目录的临时文件。
4. CLI 原子替换最终 Markdown 文件。
5. 替换成功后，只在当前 `ownerId` 命名空间内，尽力清理由 MarkItDown 清单确认、且不再被当前 Markdown 引用的旧版本目录；不删除其他所有者、无法识别的旧文件或共享资产根目录。
6. Markdown 写入或替换失败时，保留原有 Markdown 和旧资产，并尽力清理本次新版本目录。
7. 清理完成后释放发布锁；进程崩溃时由操作系统释放锁。

输出身份必须基于解析已有祖先符号链接后的绝对路径，并在 Windows 上采用不区分大小写的规范化形式，避免同一目标生成不同 `ownerId`。发布清单记录 `ownerId`，清理器必须验证清单所有者与当前锁一致。实现可以使用以 `ownerId` 为键的命名互斥量或带独占操作系统文件锁的锁文件，不得只依赖进程内锁。

版本化目录和跨进程发布锁共同保证旧 Markdown 始终引用旧资产，不会因新版本提交或并发清理而指向缺失、不匹配的内容。进程在 Markdown 替换前崩溃时仍可能留下孤立的新版本目录，但后续运行可以在持有同一发布锁时根据发布清单清理。标准输出（`stdout`）模式下，先提交资产，再写入标准输出；写出失败时同样尽力清理本次版本。

内存存储受预算限制，通过结果中的资产清单暴露字节。相同内容只存一份，但保留所有来源引用。

图片尺寸阈值只能控制是否执行视觉增强，不能控制是否保存资产。小图片或不支持解码的图片应记录为装饰性或无法解码，不能静默丢弃。

### 7.5 `IVisionAnalyzer`

当前返回自由文本的 `ILlmClient` 不足以承担文档分析。`IVisionAnalyzer` 提供类型化任务：

- `OcrPage`
- `DescribeFigure`
- `AnalyzeChart`
- `RecoverTable`

响应必须通过结构模式验证，并包含结构化区域/单元格、置信度、提供者/模型、提示词/结构模式版本、令牌或请求用量，以及重试元数据。

首个适配器复用当前 OpenAI 配置接口。整个迁移窗口内通过兼容适配器继续支持 `ILlmClient`。

栅格任务使用 `RasterVisionInput`，引用清理后的图片字节或资产 ID。支持页级输入的视觉提供者可以声明 `DocumentPageInput` 能力；该载荷包含受限来源句柄和明确页码集合，不接受工具调用方传入的任意路径。

### 7.6 `MultimodalEnricher`

增强器根据确定性信号和配置模式选择任务。它不能原地覆盖可信的原生文字；增强结果附着到原内容块，并带来源追踪和置信度。

以下内容属于实质内容：

- 文字、表格、公式和注释。
- 所有非装饰性视觉资产。

视觉资产仅在以下情况视为装饰性：

- 源文档显式标记为装饰性。
- 同一页眉/页脚资产在至少三页重复，且每页占比小于 1%。
- 页面占比小于 0.25%，并且没有替代文本、图注、关系引用或附近正文引用。

其他视觉资产默认全部为实质内容。调用方可以强制把资产标记为实质内容，但转换完成后不能压制其实质诊断。

`Auto` 模式的初始选择规则：

- 规范化原生文字少于 40 个字符，且某张栅格图像覆盖至少 60% 页面时，对该页执行 OCR。
- 页面有原生文字，同时某个图像覆盖至少 20%，或者图像带附近图注时，分类为 `Mixed`。
- 图片未标记为装饰性，且替代文本缺失或无描述性时，执行图片分析。
- 本地表格置信度低于 0.80，并且页面/区域可视觉表示时，执行表格恢复。
- DOCX 图表先提取确定性图表数据，再把视觉和已知数值一起交给视觉提供者。

这些阈值均为具名选项，在成为默认值前必须使用已提交评测语料校准。

### 7.7 `MarkdownRenderer`

渲染器只消费最终模型和资产 URI 解析器。

- 简单表格使用 Markdown 竖线表格语法。
- 含跨行、跨列、嵌套内容块或多行单元格的表格使用 HTML 表格。
- 图片包含当前最佳可访问描述，同时保留资产链接。
- 实质内容缺失时，默认输出可见警告块。
- 非实质诊断保留在结构化结果中；旧版 MCP 工具可以按需输出 HTML 注释。
- 视觉提供者生成的文字在来源追踪中标记，除非调用方要求，否则不加可见标签。

## 8. PDF 数据流

### 8.1 确定性解析

逐页记录真实媒体框/裁剪框、旋转角度、原生字符、单词、图片和几何信息。处理以页面为单位，完成后的页面状态可以释放。

布局分析必须接收真实页面尺寸：

- 先形成区域，再排序区域内的内容块。
- 全宽判断使用页面宽度，不能使用最宽内容块。
- 段落合并前后保留稳定的区域 ID 和内容块 ID。

表格检测必须发生在段落合并之前，并基于坐标：

- 支持同页多个表格和两列表格。
- `TableBlock` 记录实际消费的来源内容块 ID，不能用原始列表中的连续区间代替。
- 低置信度候选在视觉恢复成功前保持为有序文字，不能伪造表格。

### 8.2 页面模态

页面分类：

- `NativeText`：原生文字充足，布局置信度高。
- `Mixed`：原生文字加一个或多个重要视觉区域。
- `Scanned`：原生文字很少，同时存在页面主导栅格图像。
- `ComplexLayout`：本地布局置信度不足、向量内容主导，或无法只用提取图片表示。

处理规则：

- `NativeText` 仅走本地解析。
- `Mixed` 保留原生文字，只增强被选中的图像。
- `Scanned` 优先使用内嵌整页栅格图像；否则向 `IPdfPageRasterizer` 请求页面图片。
- `ComplexLayout` 使用本地内容块加渲染页面，或使用视觉提供者原生的页级文档输入。

OCR 区域按坐标合并。重叠时默认保留原生文字，只有原生区域被明确判断为空或损坏时才允许替代。

### 8.3 页面栅格化边界

PdfPig 继续承担确定性文字和几何解析，但当前安装包不提供页面栅格化，因此定义 `IPdfPageRasterizer` 适配接口。

已批准的设计最多允许引入一个新的跨平台渲染包。写入实施计划前，候选包必须满足：

- 支持 Windows、Linux、macOS x64 和 `net8.0`。
- 使用 MIT、Apache-2.0、BSD 或同等宽松许可证。
- 没有未解决的高危安全公告。
- 最近 24 个月内有维护版本。
- 支持 `Stream` 和按页范围渲染。
- 能确定性释放原生资源。
- 不要求交互式桌面安装。

若没有候选通过，则生产兜底方案是支持 `DocumentPageInput` 的已配置云端视觉提供者。

整文上传默认禁用，仅在 `AllowDocumentUpload=true` 时允许。视觉提供者接收其能力范围内的最小页集合，并在来源追踪中记录源文档曾被上传。

页面栅格化不可用，且不存在可用的 `DocumentPageInput` 兜底时（未授权整文上传、提供者不支持该能力或上传失败）：

- 保留该页。
- 产生 `PAGE_RASTERIZATION_UNAVAILABLE`。
- `Off` 或 `Auto` 模式下结果状态为 `Partial`。
- `Required` 模式下，被选中的增强无法执行，因此结果状态为 `Failed`。
- 不隐式上传整份 PDF。

项目不会自行实现 PDF 渲染。

## 9. DOCX 数据流

### 9.1 逻辑遍历

DOCX 解析器继续使用 `DocumentFormat.OpenXml 3.5.1`。它按来源顺序遍历正文子节点，并用递归行内访问器处理：

- 运行片段（`Run`）
- 超链接（`Hyperlink`）
- 字段（`Field`）
- 内容控件（`Content control`）
- 书签（`Bookmark`）
- 换行符（`Break`）
- 修订节点（`Revision node`）

首期使用接受修订后的视图：

- 插入内容纳入模型并记录修订来源。
- 删除内容不纳入模型。
- 报告插入/删除数量。
- 首期不支持渲染全部修订标记。

### 9.2 关系与补充文档部件

- 超链接保留显示文字和解析后的目标，但不访问目标。
- 行内/锚定绘图解析图片关系和替代文本。
- `ChartPart` 在视觉分析前提取标题、分类、数据系列和缓存值。
- SmartArt 在可用时从图示数据部件提取文字。
- 文本框在其逻辑锚点位置插入。
- 脚注/尾注输出 Markdown 注释引用和定义。
- 批注使用第 7.3 节的补充内容规则。
- 页眉/页脚使用第 7.3 节的类型化补充内容与去重规则。

外部关系、模板和链接图片只记录，不下载。

### 9.3 列表、表格和公式

列表必须解析编号定义、层级、起始值和样式继承，以确定：

- 有序或无序。
- 嵌套层级。
- 起始编号。

表格先转换成逻辑网格，保留网格跨度和垂直合并：

- 简单矩形网格输出 Markdown。
- 合并或嵌套网格输出 HTML。

OMML 公式产生 `EquationBlock`，保存原始 OMML 和提取的线性文字：

- 只有确定性转换得到经过验证的表示时，才输出 Markdown 数学公式。
- 否则保留可读线性形式并产生诊断。
- 公式不能静默丢弃。

### 9.4 视觉增强

图片描述优先使用替代文本。`image`、`picture` 或文件名等占位值不算有效描述。

图表同时使用确定性图表数据和可选视觉解释。视觉提供者输出可以总结已知数值，但如果引入图表数据中不存在的数值事实，必须标记低置信度。

DOCX 不提供可靠的最终页面坐标，因此转换器保证逻辑顺序和锚点顺序，不承诺 Word 渲染后的分页。

## 10. 保真状态、诊断和错误处理

### 10.1 保真状态

- `NotEvaluated`：仅用于未进入共享多模态保真评估的旧管线、尚未接入的格式或第三方转换器；表示转换调用成功，不表示内容完整。
- `Complete`：每个实质内容块都有确定性内容、可靠的源文档替代描述，或成功完成所需增强。
- `Partial`：Markdown 可用，但至少一个实质内容块只有明确诊断，或只保留原始资产而没有完整解释。
- `Failed`：输入无法安全打开/解析，资产输出无法提交，或 `Required` 模式下的增强失败。

凡进入共享 PDF/DOCX 多模态管线的转换，必须计算出 `Complete`、`Partial` 或 `Failed`，不能返回 `NotEvaluated`。

### 10.2 视觉模式（VisionMode）

- `Off`：只做确定性解析。
- `Auto`：视觉提供者可用时分析被选中的内容；提供者失败产生 `Partial`。
- `Required`：被选中的内容必须分析成功，否则为 `Failed`。

公开的 `ConversionOptions.VisionMode` 可空，`null` 表示未显式设置；进入 `ConversionContext` 时必须解析成非空值。多模态管线中，显式模式优先；未显式设置且存在 `VisionAnalyzer` 或由旧 `LlmClient` 创建的兼容提供者时选择 `Auto`，没有视觉提供者时选择 `Off`。CLI 仅提供旧 LLM 密钥时，默认行为仍先受迁移期管线规则约束。

三种模式使用相同的任务选择规则：

- `Off` 不发送任务。实质内容缺少确定性描述时，产生 `VISION_DISABLED` 并标记为 `Partial`。
- 显式选择 `Auto` 但没有视觉提供者：没有任务被选中时不额外产生提供者诊断；有任务时产生 `VISION_PROVIDER_MISSING` 并标记为 `Partial`。
- 选择 `Required` 但没有视觉提供者：没有任务被选中时不额外产生提供者诊断；有任务时为 `Failed`。

最终状态始终由解析、资产、资源和增强阶段的全部诊断根据第 10.1 节共同计算。“没有视觉任务”绝不能强制判定为 `Complete`。

### 10.3 诊断契约

每条诊断包含稳定代码、严重度、消息、来源位置、可恢复性和可选异常类别。

首批实质诊断代码：

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
- `FILE_NOT_FOUND`
- `UNSUPPORTED_FORMAT`
- `MULTIMODAL_FORMAT_UNSUPPORTED`
- `CONVERSION_FAILED`
- `OUTPUT_WRITE_FAILED`

消息可以增加上下文，但诊断代码的含义是稳定公共契约。

### 10.4 故障隔离

- `Auto` 模式下，一个视觉任务失败只影响该任务，不影响整份文档。
- 本地无法解码的图片仍进入资产清单，并附诊断。
- 单页或 DOCX 补充部件失败时，只要剩余模型安全且连贯，可以返回 `Partial`。
- 损坏的文档包根、无效 PDF 交叉引用或资产提交失败属于致命失败。
- 取消操作不能被包装为 `ConversionException`。

视觉提供者只对 `408`、`429` 和 `5xx` 重试，最多两次，使用退避和随机抖动。其他 `4xx` 不重试。

### 10.5 失败状态对各入口的行为

`DocumentConversionResult` 作为 `Complete`、`Partial` 或 `NotEvaluated` 的成功返回值；`NotEvaluated` 只适用于未经过新保真评估的兼容路径。`Failed` 按失败发生阶段沿用现有异常契约：

- 库 API 的入口参数和输入选择错误继续使用现有异常：无效选项抛 `ArgumentException`，文件不存在抛 `FileNotFoundException`，格式不支持抛 `UnsupportedFormatException`。这些错误发生在多模态上下文建立前，不承诺携带失败报告。
- 多模态上下文建立后发生的转换 `Failed` 统一抛出 `ConversionException`；异常附带非空 `ConversionFailureReport`，其中包含 `Status=Failed`、诊断和已产生的用量，并回滚尚未提交的资产事务。
- CLI 对输入不存在或格式不支持继续返回退出码 1；解析失败、实质资产写入失败、输出超限和 `Required` 视觉失败返回退出码 2；`Partial` 配合 `--fail-on-partial` 返回退出码 3。
- 旧版 MCP 工具继续返回当前错误文本，不发布资产。
- `convert_to_markdown_detailed` 捕获转换异常并返回稳定的结构化响应。`Failed` 时 `Status=Failed`、`Markdown=null`、`AssetUris=[]`，同时返回至少一条致命诊断和非空用量对象。

CLI 在 `Engine` 已成功提交资产后写入或替换 Markdown 失败，属于入口发布失败，不会追溯修改已经产生的 `DocumentConversionResult`。CLI 返回退出码 2、产生 `OUTPUT_WRITE_FAILED`，并按第 7.4 节清理本次唯一资产版本；清理失败只留下可识别的孤立版本，不能破坏旧输出。

`RESOURCE_LIMIT_EXCEEDED` 按资源类型判定：

- 输入大小、页数、Markdown 输出长度，或实质资产存储上限被突破时为 `Failed`。
- `Auto` 模式耗尽视觉任务预算时停止新增调用，为剩余待分析实质内容记录诊断并返回 `Partial`。
- `Required` 模式耗尽视觉任务预算时为 `Failed`。
- 装饰性资产超限可以跳过并记录非实质诊断，不单独降低保真状态。

## 11. 资源和隐私策略

### 11.1 默认限制

| 限制 | 默认值 |
|---|---:|
| 输入大小 | 256 MiB |
| PDF 页数 | 500 |
| 资产数量 | 1,000 |
| 单资产大小 | 32 MiB |
| 资产总大小 | 512 MiB |
| 单图解码像素 | 50 MP |
| 单文档视觉任务 | 100 |
| 视觉并发 | 2 |
| 单视觉任务总超时 | 90 秒 |
| 整体转换时限 | 15 分钟 |
| Markdown 字符数 | 20,000,000 |

库 API 调用方可以配置限制；CLI 和 MCP 必须从有限默认值开始。能提前知道大小时，必须在分配内存或提交视觉提供者前检查。

库 API 的整体时限从 `Engine` 入口开始；CLI 和 MCP 则从单项输入或请求开始时创建同一截止时间，并把扣除发布锁等待后的剩余时间传给 `Engine`。时限包含输入流暂存、解析、全部重试、渲染、资产提交和入口发布。

90 秒视觉任务超时覆盖该任务的全部尝试，不是每次重试各 90 秒；同时受剩余整体时限限制。

### 11.2 隐私控制

- 默认只上传任务需要的页面或裁剪区域。
- 只有 `AllowDocumentUpload=true` 时才允许上传源 PDF。
- 视觉提供者的页范围限制和实际上传页集合必须记录。
- 上传栅格图片前重新编码，删除 EXIF、GPS 和无关元数据。
- 默认不访问 DOCX 外部关系。
- 普通日志不能写 API 密钥、源图片字节或完整的提供者响应正文。
- 缓存键包含清理后输入哈希、任务类型、提供者/模型和结构模式版本。
- MCP 默认关闭提供者缓存，除非管理员配置加密或访问受控的缓存。

## 12. 公共契约和入口

### 12.1 保持源码兼容的模型变更

`DocumentConversionRequest` 保留现有属性，并直接新增以下可选属性：

- `Options`
- `AssetStore`
- `VisionAnalyzer`

同时暴露由 `Engine` 填充的只读 `Context` 属性，只有 `Core` 内部可以初始化该属性。

`ConversionOptions.VisionMode` 的类型为可空 `VisionMode?`：`null` 表示使用第 10.2 节的解析规则，显式 `Off` 与未设置是两个不同状态。`FidelityStatus.NotEvaluated` 的数值为 0，保证未初始化的新附加属性不会把旧转换结果误标为 `Complete`。

`Engine` 先解析 `PipelineMode`，再按管线分支执行组合校验和适配，不能在确定管线前无条件安装适配器。

多模态管线的组合规则：

- `AssetBasePath` 在 `AssetStore` 为空时适配为目录存储。
- 同时提供 `AssetBasePath` 和 `AssetStore` 时抛 `ArgumentException`。
- 同时提供 `VisionAnalyzer` 和旧 `LlmClient` 时抛 `ArgumentException`。
- 只有 `LlmClient` 时，`Core` 安装兼容视觉适配器。

旧管线直接沿用现有 `AssetBasePath` 和 `LlmClient` 路径，不创建 `IAssetStore` 或 `IVisionAnalyzer` 适配器；新选项的拒绝规则见第 12.3 节。

`DocumentConversionResult` 保留现有位置参数构造函数和属性，新增仅初始化属性：

- `Assets`
- `Diagnostics`
- `FidelityStatus`
- `Usage`

为保持旧转换器兼容，新增集合默认非空且为空，`Usage` 默认使用零值对象；未参与共享保真评估的旧管线、尚未接入的格式或第三方转换器结果将 `FidelityStatus` 默认为 `NotEvaluated`。

新增公开的 `ConversionFailureReport`，字段固定为：

| 字段 | 契约 |
|---|---|
| `FidelityStatus Status` | 始终为 `Failed` |
| `string? Kind` | 能识别转换器时填写，否则为空 |
| `IReadOnlyList<ConversionDiagnostic> Diagnostics` | 非空且至少包含一条致命诊断 |
| `ConversionUsage Usage` | 非空；没有外部调用时为零值对象 |
| `string OperationId` | 非空日志关联 ID，不是资源访问令牌 |

`ConversionFailureReport` 不包含 Markdown 或资产，因为 `Failed` 路径必须回滚资产事务。

`ConversionException` 新增可空的 `ConversionFailureReport? FailureReport` 属性。旧构造函数继续可用，因此由旧版代码或第三方转换器直接创建的异常允许该属性为空；多模态 `Engine` 在建立上下文后的所有 `Failed` 路径都必须附带非空报告。

MCP 详细工具层遇到空 `FailureReport` 时必须合成 `ConversionFailureReport`，不能退化成字段形状不同的协议错误：

- `FileNotFoundException` 映射为 `FILE_NOT_FOUND`。
- `UnsupportedFormatException` 映射为 `UNSUPPORTED_FORMAT`。
- 空报告的 `ConversionException` 或其他已清理内部失败映射为 `CONVERSION_FAILED`。
- 合成报告使用 MCP 请求开始时创建的操作 ID、空 `Kind`、零值 `Usage` 和至少一条致命诊断。
- 取消操作继续走 MCP 取消语义，不合成为 `Failed`。

MCP 的 `convert_to_markdown_detailed` 使用稳定响应形状：

| 字段 | `NotEvaluated`/`Complete`/`Partial` | `Failed` |
|---|---|---|
| `Status` | 对应成功状态 | `Failed` |
| `Markdown` | 非空 | `null` |
| `AssetUris` | 非空或空列表 | 空列表 |
| `Diagnostics` | 非空或空列表 | 至少一条致命诊断 |
| `Usage` | 非空零值或实际用量 | 非空零值或失败前用量 |

该 MCP 响应由 `DocumentConversionResult`、`ConversionFailureReport` 或上述合成报告映射，不能同时携带成功结果和失败报告。

`ILlmClient` 继续作为兼容 API 保留；新的 PDF/DOCX 能力内部使用 `IVisionAnalyzer`。

### 12.2 资产交付

本节只描述 `PipelineMode=Multimodal` 的行为。预览阶段默认使用旧管线（`legacy`）的快捷重载继续保持现有资产行为；显式切换到多模态管线（`multimodal`）后才应用下表。

首期只有 PDF 和 DOCX 进入共享多模态管线。其他已支持格式在 `PipelineMode=Multimodal` 下仍调用原转换器，并返回 `NotEvaluated` 和 `MULTIMODAL_FORMAT_UNSUPPORTED` 诊断，不能伪装成 `Complete`；`Off` 或 `Auto` 模式下 Markdown 仍可成功返回，CLI 同时向标准错误写警告。`Required` 模式要求共享管线能力，因此这些格式以同一诊断代码返回 `Failed`。详细 MCP 工具对这些格式也遵循该规则。

| 入口 | 资产行为 |
|---|---|
| CLI | 写入 `{output-stem}_files/{ownerId}/{publicationId}` 不可变版本目录，Markdown 使用相对 URI |
| 库 API + 显式目录 | 使用调用方指定的目录存储 |
| 库 API + 未指定目录 | 使用受限内存存储；Markdown 使用 `asset://` URI，字节位于 `Assets` |
| MCP | 使用受限临时存储，返回不透明 `markitdown://` 资源 URI |
| 嵌套转换 | 使用子命名空间，共享父级预算与生命周期 |

多模态路径和 `Stream` 快捷重载使用相同的有限默认选项。默认使用受限内存存储，不再因为未提供目录而禁用资产提取。

### 12.3 CLI

保留现有 LLM 选项，并新增：

- `--vision off|auto|required`
- `--fail-on-partial`
- `--diagnostics <path>`
- `--allow-document-upload`
- `--pipeline legacy|multimodal`

`Partial` 默认仍返回退出码 0 以兼容脚本，同时向标准错误（`stderr`）写警告。`--fail-on-partial` 使 `Partial` 返回退出码 3。

批量输入继续逐项处理，取消操作除外。每项结果先归类，再按固定优先级聚合最终退出码，不能由最后一个输入覆盖先前结果：

1. 任一转换、资产提交、Markdown 发布或诊断文件发布发生硬失败：退出码 2。
2. 否则，任一输入不存在或格式不支持：退出码 1。
3. 否则，启用 `--fail-on-partial` 且任一输入为 `Partial`：退出码 3。
4. 否则：退出码 0。

`--diagnostics <path>` 对单文件和批量输入使用同一种版本化 JSON 文档。顶层固定包含 `SchemaVersion` 和按输入顺序排列的 `Entries`；每个条目固定包含 `Input`、可空 `Output`、`Status`、`Diagnostics` 和 `Usage`。`NotEvaluated`、`Complete` 和 `Partial` 都写入结果中的全部诊断，列表可以为空；转换失败和入口发布失败写 `Failed` 及至少一条致命诊断；上下文建立前的已知错误使用第 12.1 节的合成诊断规则。CLI 使用第 7.4 节的跨进程发布锁，先写临时文件，再原子替换诊断文件；诊断路径与任一 Markdown 输出路径相同时属于用法错误。全局命令行用法错误发生在逐项处理前，不创建诊断文件；诊断文件自身发布失败返回退出码 2，但不回滚已经成功发布的独立输出。

`--allow-document-upload` 默认为 `false`，仅与有效视觉模式 `Auto`/`Required` 配合，是 CLI 对视觉提供者原生 PDF 输入的显式授权。解析完 `--llm-key` 带来的默认模式后，如果有效模式为 `Off` 且该选项为 `true`，CLI 必须返回用法错误，不能静默忽略授权。

库 API 使用 `ConversionOptions.Privacy.AllowDocumentUpload`，默认同样为 `false`。在多模态管线中，如果有效 `VisionMode=Off` 且 `AllowDocumentUpload=true`，上下文创建阶段抛出 `ArgumentException`。

管线迁移规则：

- 预览版本：现有 CLI、库 API 快捷重载和旧版 MCP 工具默认使用 `legacy`。
- `convert_to_markdown_detailed` 始终使用 `multimodal`。
- `ConversionOptions.PipelineMode` 保留到下一个主版本。

预览阶段，显式使用以下任一新参数都要求同时指定 `--pipeline multimodal`：

- `--vision`
- `--allow-document-upload`
- `--fail-on-partial`
- `--diagnostics`

与显式或默认的 `legacy` 组合时属于 CLI 用法错误，不能静默切换管线或忽略选项。

只提供旧 `--llm-key` 时保持旧管线行为，包括旧版 PDF/DOCX 不消费文档视觉能力。`--llm-key --pipeline multimodal` 会创建视觉适配器；没有显式指定 `--vision` 时选择 `Auto`。

库 API 遵循相同规则。`Engine` 先解析 `PipelineMode`：在 `PipelineMode=Legacy` 时提供 `VisionAnalyzer`、显式设置任一 `VisionMode`（包括 `Off`）、设置 `Privacy.AllowDocumentUpload=true` 或提供新的 `AssetStore`，都在上下文创建阶段抛出 `ArgumentException`；校验通过后，旧 `LlmClient` 和 `AssetBasePath` 直接走旧路径，不安装兼容适配器。只有 `PipelineMode=Multimodal` 才执行第 12.1 节的适配规则。

### 12.4 MCP

现有仅返回 Markdown 的 `convert_to_markdown` 保留用于兼容：

- 预览版本遵循服务器 `PipelineMode`，默认使用 `legacy`。
- 配置为 `multimodal` 时使用共享管线。
- 多模态模式下把实质诊断摘要写成 HTML 注释。
- 多模态模式下资产使用下述资源 URI。

新增 `convert_to_markdown_detailed`。对通过参数验证并进入转换管线的调用，它始终返回以下字段：

- `Status`
- `Markdown`
- `AssetUris`
- `Diagnostics`
- `Usage`

`NotEvaluated`/`Complete`/`Partial` 与 `Failed` 的可空性和空列表规则以第 12.1 节的稳定响应表为唯一契约；字段不会因为失败而缺席。请求模式校验失败仍使用 MCP 的无效参数错误，不伪装成转换结果。

它接受 `allow_document_upload` 参数，默认为 `false`；这是 MCP 对视觉提供者原生 PDF 输入的唯一授权入口。详细工具的有效视觉模式来自服务器 `ConversionOptions.VisionMode`：服务器未显式配置时，有视觉提供者则为 `Auto`，否则为 `Off`；该参数只授予上传权限，不能改变视觉模式。有效模式为 `Off` 且该参数为 `true` 时，请求在转换前以 MCP 无效参数错误拒绝。旧版工具永不允许整文上传。

资产通过 MCP 资源模板暴露：

`markitdown://conversion/{conversionId}/assets/{assetId}`

多模态 Markdown 内也使用同一 URI。

生命周期和访问控制：

- 临时转换从成功发布结果时开始计时，默认 15 分钟过期。
- 管理员可以配置 1 到 60 分钟。
- 临时资产始终受共享资产字节上限约束。
- 进程默认最多容纳 128 个正在转换或尚未过期的转换，输入暂存、资产暂存和已发布临时资产合计最多 2 GiB；管理员可以下调或上调这两个有限值。
- 根事务开始前必须原子预留活动转换名额；每次写入输入或资产暂存前必须按实际新增字节增量预留全局字节。清理已过期项后仍无法预留时，当前转换以 `RESOURCE_LIMIT_EXCEEDED` 失败并回滚自身事务，不能继续写入，也不能驱逐尚未过期的结果。
- 提交把暂存字节转为已发布字节但不重复计数；回滚、取消和过期清理都必须释放对应名额与字节。并发预留与释放使用同一个原子账户，不能先写磁盘后补记预算。
- 转换 ID 是独立于操作 ID 的访问令牌，至少包含 256 位密码学随机性，并携带由进程本地密钥认证的过期时间声明。
- 转换 ID 绑定服务器进程；传输层提供已认证主体时同时绑定该主体。
- 服务端内部维护转换 ID 到操作 ID 的映射，但普通日志、追踪和指标永不记录转换 ID、完整资源 URI 或它们的可逆形式。
- 资源处理器不接受路径，只能解析同一次转换内的资产。
- 资源读取先获取租约；清理先把转换标记为过期并拒绝新租约，等待现有读取释放后再删除文件和扣减全局字节。
- 同一进程内，签名有效但已过期的转换 ID 即使文件已清理也返回 `CONVERSION_EXPIRED`；伪造或未知 ID 返回 `CONVERSION_NOT_FOUND`。
- 进程重启会更换本地密钥并清理上个进程遗留目录，因此旧 ID 返回 `CONVERSION_NOT_FOUND`，不承诺跨重启区分过期状态。
- 资源访问时执行惰性清理；后台每分钟执行一次清理。

`CONVERSION_EXPIRED` 和 `CONVERSION_NOT_FOUND` 是 MCP 资源读取响应代码，不属于第 10.3 节的转换诊断代码。

## 13. 可观测性

结构化事件使用操作 ID，记录：

- 解析、增强和渲染耗时。
- 页面与内容块数量。
- 资产保留、去重、跳过和无法解码的数量。
- 视觉任务类型、提供者请求 ID、尝试次数、延迟和用量。
- 缓存命中/未命中。
- 限制与诊断代码。
- 最终保真状态。

默认事件不能包含原始文档文字或图片字节。

## 14. 测试策略

### 14.1 评测语料

提交的评测语料同时包含匿名化真实文档和生成式对抗文档。

初始质量门至少包含：

- 24 个 PDF。
- 16 个 DOCX。

PDF 平均分为四组：

- 原生文字（`Native`）。
- 扫描文档（`Scanned`）。
- 混合文档（`Mixed`）。
- 复杂布局/表格密集。

DOCX 平均分为四组：

- 文字/关系。
- 图片/图表。
- 列表/表格。
- 注释/修订。

每种格式至少一半来自独立制作或匿名化真实文档，不能全部由转换器自己的测试样例生成器产生。

PDF 覆盖：

- 原生单栏、多栏、跨栏标题和旋转页。
- 清晰扫描、退化扫描、中英扫描和混合页面。
- 两列表格、多表格、合并单元格和低置信度表格。
- 图片、图注、图表、公式、向量主导内容和不支持的图片编码。

DOCX 覆盖：

- 内嵌/浮动图片、替代文本、图表、SmartArt、文本框。
- 超链接、字段、内容控件、修订和 OMML。
- 嵌套列表、合并表格、页眉页脚、注释和批注。

每个测试样例保存：

- 规范化 `DocumentModel` 基准 JSON。
- Markdown 快照。
- 资产预期。
- 来源位置。
- 预期诊断。

### 14.2 确定性测试

- 单元测试覆盖解析器、内容块顺序、表格网格、资产事务、限制和诊断映射。
- 契约测试使用假的 HTTP/视觉提供者，覆盖请求、结构化响应、超时、重试、拒答和无效输出。
- 集成测试比较路径/`Stream`，并在规范化入口特有资产 URI 后比较 CLI、库 API 和 MCP。
- 兼容测试验证 PDF/DOCX 多模态结果永不为 `NotEvaluated`，尚未接入的格式在 `Off`/`Auto` 下返回 `NotEvaluated`，在 `Required` 下返回 `Failed`。
- 资源测试覆盖取消、不可随机定位的 `Stream`、超大输入、解压压力、高像素图片、资产限制和路径边界。
- CLI 测试覆盖批量退出码优先级、诊断 JSON 的单文件/批量形状、Markdown 发布失败、版本化资产回滚和孤立版本清理。
- CLI 并发测试使用两个进程竞争同一规范化输出路径，并覆盖同目录下相同文件名主体、不同扩展名的输出，验证发布锁和 `ownerId` 隔离覆盖资产提交、Markdown 替换和旧版本清理，且不会删除另一个进程正在发布或引用的版本。
- MCP 测试覆盖进程级数量/字节并发预留、超限拒绝、签名过期令牌、伪造令牌、进程重启语义，以及读取租约与清理的竞争。
- CLI 进程测试必须有硬超时，并终止整个进程树。

真实视觉提供者测试只在手动或定时工作流运行，不阻塞普通拉取请求（PR）。

运行口径：

- 确定性保留、资产召回、DOCX 元素保留、原生阅读顺序、限制和诊断在持续集成（CI）中使用 `VisionMode=Off` 或适当的假提供者。
- OCR 字符错误率、视觉恢复表格 F1 和混合页面最终顺序在发布资格评测中使用 `VisionMode=Auto`。
- 发布用提供者、模型、提示词版本和结构模式版本必须固定。
- 每个真实质量样例运行三次，中位数必须达标。
- 任一次运行都不能静默遗漏标注的实质内容块。
- 固定提供者或提示词发生变更后、发布资格评测前，必须记录新的基线。
- CLI、库 API 和 MCP 一致性门显式为每个入口设置 `PipelineMode=Multimodal`。
- 旧管线输出只做向后兼容回归，不混入新管线质量指标。

### 14.3 验收指标定义

资产召回率：结果包含对应字节，或包含可解析资产引用，并具有预期来源关系。

DOCX 元素保留率：必须有正确语义内容块、原始元素表示，或链接到预期来源的可解析资产。只有诊断不算保留；失败诊断覆盖率单独统计。

表格单元格 F1：比较规范化单元格文字、行、列和跨行/跨列信息。

阅读顺序准确率：对标注的非装饰性内容块计算两两顺序准确率。

### 14.4 验收门

| 指标 | 必须达到 |
|---|---:|
| 标注视觉资产召回率 | 100% |
| 标注 DOCX 元素保留率 | 100% |
| 清晰扫描 OCR 字符错误率 | <= 3% |
| 退化扫描 OCR 字符错误率 | <= 8% |
| 表格单元格 F1 | >= 95% |
| 标注阅读顺序准确率 | >= 95% |
| 强制实质内容失败的诊断覆盖率 | 100% |
| 原生文字页不必要视觉调用 | 0 |
| 规范化 CLI/库 API/MCP 一致性 | 100% |
| 未解决高危依赖公告 | 0 |

无视觉提供者的原生文字 PDF 和普通 DOCX 转换，p95 不得超过已建立基线的 1.5 倍。云端路径由总时限和预算控制，不使用固定的端到端延迟门。

## 15. 交付顺序

### 阶段 0：基线与红测试

- 提交标注评测语料、基准模型格式和评分工具。
- 为所有已确认的静默丢失和排序问题建立失败测试。
- 建立带硬超时的 CLI 子进程测试框架。

### 阶段 1：共享契约

- 增加输入规范化、上下文、限制、资产、诊断、保真状态和结果附加属性。
- 引入共享基础设施期间，保持旧版转换器行为。

### 阶段 2：PDF 确定性管线

- 实现路径/`Stream` 等价。
- 实现页面模型和正确页面几何信息。
- 实现区域优先的阅读顺序。
- 实现坐标表格检测。
- 实现完整资产清单。
- 增加页面模态分类和栅格化适配器边界。

### 阶段 3：DOCX 确定性管线

- 实现递归行内遍历。
- 实现关系感知资产。
- 实现编号、表格、补充部件、图表数据和公式保留。

### 阶段 4：多模态增强

- 增加类型化视觉提供者。
- 增加上传前图片规范化。
- 增加任务选择、缓存、重试、用量和部分成功结果。
- 页面栅格化器候选只有通过依赖门后才能加入。

### 阶段 5：入口整合

- CLI、库 API、MCP 和嵌套转换统一使用一套上下文策略。
- 增加 CLI 结构化诊断和 MCP 资产资源。

### 阶段 6：质量与迁移

- 运行评测语料、资源、安全和性能门。
- 预览版本：现有入口默认使用 `legacy`，可显式选择 `multimodal`；详细 MCP 工具始终使用 `multimodal`。
- 默认切换阶段：全部验收门通过，且一个预览版本没有未解决的关键回归后，默认切换为 `multimodal`；`legacy` 继续显式保留一个次版本。
- 稳定期次版本：`multimodal` 为默认，继续保留 `legacy` 并记录迁移证据。
- 清理主版本：只在下一个主版本移除 `PipelineMode`、`--pipeline` 和重复的旧版 PDF/DOCX 路径。

## 16. 风险与缓解

| 风险 | 缓解措施 |
|---|---|
| 视觉提供者输出不确定 | 类型化结构模式、置信度、假提供者 CI、PR 外真实测试 |
| PDF 页面栅格化器带来原生部署风险 | 最多一个依赖，并执行跨平台、许可证、维护和安全门 |
| DOCX 直接解析复杂度上升 | 小型中间表示、聚焦访问器、真实基准语料、不承诺像素布局 |
| 云端泄露数据或成本失控 | 最小范围上传、显式整文授权、元数据清理、有限任务、缓存 |
| 丰富结果破坏调用方 | 附加属性、保留构造函数、保留旧版 CLI/MCP 行为 |
| 质量管线拖慢普通文档 | 本地优先任务选择和无云 p95 门 |
| `Partial` 或 `NotEvaluated` 被误认为 `Complete` | 保真状态、实质内容警告、旧格式警告、`--fail-on-partial` |
| 现有脆弱图片依赖进入新链路 | 发布前清除高危公告，否则依赖门失败 |

## 17. 完成条件

只有同时满足以下条件，优化才算完成：

1. 已提交评测语料上的全部验收指标通过。
2. PDF/DOCX 路径和 `Stream` 输入在 URI 规范化后等价。
3. 所有标注实质资产和 DOCX 元素均被保留，或在无法保留时附带准确诊断。
4. CLI、库 API、MCP 和嵌套转换共享限制与视觉策略。
5. `Auto` 模式视觉提供者失败时返回连贯的 `Partial` 结果并保留资产。
6. `Required` 模式视觉提供者失败行为明确且确定。
7. 全量构建、测试、格式化和静态分析门通过。
8. CLI 测试进程不可能超过其配置超时存活。
9. 没有未解决的高危依赖公告。
10. 公共迁移说明记录新增结果字段、CLI 选项、MCP 详细工具/资源和旧管线迁移路径。
