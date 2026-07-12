"""Minimal, restricted Docling JSONL worker.

The worker accepts document bytes, never a caller-provided path, and writes only
versioned JSON responses to stdout. Logging belongs on stderr.
"""
import base64
import json
import os
import sys
import tempfile
import uuid

_converter = None


def respond(request_id, success, document_json=None, error_code=None, error_message=None):
    return {
        "requestId": request_id,
        "protocolVersion": "1",
        "success": success,
        "documentJson": document_json,
        "errorCode": error_code,
        "errorMessage": error_message,
    }


def convert(request):
    global _converter
    if _converter is None:
        from docling.document_converter import DocumentConverter
        _converter = DocumentConverter()

    content = base64.b64decode(request["contentBase64"], validate=True)
    suffix = os.path.splitext(request.get("filename", "input.bin"))[1] or ".bin"
    with tempfile.NamedTemporaryFile(prefix="markitdown-", suffix=suffix, delete=False) as handle:
        handle.write(content)
        path = handle.name
    try:
        result = DocumentConverter().convert(path)
        return json.dumps(result.document.export_to_dict(), ensure_ascii=False, separators=(",", ":"))
    finally:
        try:
            os.unlink(path)
        except OSError:
            pass


def main():
    for line in sys.stdin:
        request = {}
        try:
            request = json.loads(line)
            request_id = request.get("requestId") or str(uuid.uuid4())
            if request.get("protocolVersion") != "1":
                raise ValueError("unsupported protocolVersion")
            document_json = convert(request)
            output = respond(request_id, True, document_json=document_json)
        except Exception as exc:
            print(f"docling worker error: {exc}", file=sys.stderr, flush=True)
            output = respond(request.get("requestId", ""), False, error_code="worker_error", error_message=str(exc))
        print(json.dumps(output, ensure_ascii=False, separators=(",", ":")), flush=True)


if __name__ == "__main__":
    main()
