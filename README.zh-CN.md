# MarkItDown C#

[English](./README.md)

一个 C# 库和 CLI 工具，用于将文件和 URL 转换为 Markdown。基于 [microsoft/markitdown](https://github.com/microsoft/markitdown) 移植，由 .NET 8 驱动。

## 特性

- **CLI 优先** — 一条命令即可在终端完成转换
- **20+ 格式** — 覆盖文档、数据、媒体和网页内容
- **保留结构** — 标题、列表、链接、表格、代码块完整保留
- **噪声过滤** — 自动去除 HTML 中的导航栏、侧边栏等干扰内容
- **LLM 描述** — 可选通过 OpenAI 兼容 API 为图片/音频生成文字描述
- **可扩展** — 插件化转换器架构，轻松添加新格式

## 支持的格式

| 类别 | 扩展名 / 类型 |
|------|---------------|
| 文档 | `.docx` `.pptx` `.xlsx` `.csv` `.msg` `.pdf` `.html` `.htm` |
| 数据 | `.json` `.jsonl` `.xml` `.rss` `.atom` `.ipynb` `.epub` `.zip` `.md` |
| 媒体 | `.jpg` `.jpeg` `.png` `.mp3` `.wav` `.m4a` |
| 网页 | URL（`http://`、`https://`）、维基百科文章 |

## 环境要求

- .NET SDK 8.0（`8.0.401` 或兼容的 `8.0.x`）

## 安装

```bash
git clone https://github.com/WenElevating/markitdown-csharp.git
cd markitdown-csharp
dotnet restore MarkItDown.sln --configfile NuGet.Config
```

## 使用

### 命令行

转换文件并输出到终端：

```bash
dotnet run --project src/MarkItDown.Cli -- document.pdf
```

写入文件：

```bash
dotnet run --project src/MarkItDown.Cli -- document.docx -o output.md
```

批量转换到目录：

```bash
dotnet run --project src/MarkItDown.Cli -- *.html -o markdown/
```

转换网页：

```bash
dotnet run --project src/MarkItDown.Cli -- https://example.com
```

启用 LLM 图片/音频描述：

```bash
dotnet run --project src/MarkItDown.Cli -- photo.jpg --llm-key sk-... --llm-model gpt-4o
```

列出所有支持的格式：

```bash
dotnet run --project src/MarkItDown.Cli -- --list-formats
```

### 作为库使用

```csharp
using MarkItDown.Core;
using MarkItDown.Converters.Pdf;

var engine = new MarkItDownEngine(builder => builder.Add(new PdfConverter()));
var result = await engine.ConvertAsync("document.pdf");

Console.WriteLine(result.Markdown);
Console.WriteLine(result.Title);    // 提取的标题（可能为 null）
Console.WriteLine(result.Kind);     // 格式标识，例如 "Pdf"
```

## 命令行选项

```
markitdown <path> [<path>...] [options]

参数:
  <paths>          输入文件路径或 URL

选项:
  -o, --output       输出文件或目录（默认输出到终端）
  --list-formats     列出所有支持的格式
  --llm-key          OpenAI API 密钥（启用 LLM 描述功能）
  --llm-model        LLM 模型名称（默认 gpt-4o）
  --llm-endpoint     自定义 API 端点（如 Azure OpenAI）
  -h, --help         显示帮助信息
  -V, --version      显示版本号
```

## 项目结构

```
src/
  MarkItDown.Core/               引擎、转换器接口、请求/结果模型
  MarkItDown.Cli/                CLI 入口
  MarkItDown.Converters.Html/    HTML 转 Markdown（HtmlAgilityPack）
  MarkItDown.Converters.Pdf/     PDF 转 Markdown（iText7）
  MarkItDown.Converters.Office/  docx、pptx、xlsx、csv、msg
  MarkItDown.Converters.Data/    json、jsonl、xml、rss、ipynb、epub、zip、md
  MarkItDown.Converters.Media/   图片、音频
  MarkItDown.Converters.Web/     URL、维基百科
  MarkItDown.Llm/                OpenAI 兼容 API 客户端
  MarkItDown.McpServer/          MCP 服务器实现

tests/                           测试项目（xUnit）+ 共享测试夹具
```

## 测试

```bash
dotnet restore MarkItDown.sln --configfile NuGet.Config

# 运行所有测试
dotnet test MarkItDown.sln --no-restore

# 运行指定测试项目
dotnet test tests/MarkItDown.Converters.Html.Tests --no-restore
```

## 架构

转换系统基于插件化架构：

```
IConverter → BaseConverter → 具体转换器（HtmlConverter、PdfConverter、...）
                                    ↓
ConverterRegistry → MarkItDownEngine → CLI / 库调用方
```

每个转换器声明其支持的文件扩展名和 MIME 类型。引擎选择第一个匹配的转换器并委托转换。添加新格式只需实现 `BaseConverter` 并通过 builder 注册即可。

## 致谢

本项目是 [microsoft/markitdown](https://github.com/microsoft/markitdown) 的 C# 移植版本。特别感谢原作者设计了一套优秀的转换架构。

## 许可证

MIT
