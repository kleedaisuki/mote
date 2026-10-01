"""Prepare an isolated Engine copy and Native AOT attribution probe.

Usage from the repository root:
    python benchmarks/NativeJsonOpenAttribution/prepare.py
    dotnet publish .temp/native-json-open-attribution/Probe.csproj -c Release \
        -r win-x64 -p:PublishAot=true \
        -o .temp/native-json-open-attribution/publish

No production source is modified. Exact source substitutions fail closed if the
Engine changes; outputs and copied source remain under repository .temp.
"""

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import uuid

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent


def artifact(value, area):
    """Reject artifacts outside the chosen repository scratch tree or linked ancestry."""
    path = Path(value).absolute()
    for entry in (path, *path.parents):
        if entry.is_symlink() or getattr(entry, "is_junction", lambda: False)():
            raise ValueError("Linked artifact ancestry")
    path = path.resolve()
    path.relative_to((ROOT / area).resolve())
    return path


def replace_once(text, before, after):
    """Require the reviewed source shape instead of silently producing partial timers."""
    if text.count(before) != 1:
        raise ValueError(f"Source shape changed: {before[:60]}")
    return text.replace(before, after)


def prepare(directory):
    """Copy, namespace-isolate and instrument fixed read/hash/decode/chunk/rope phases."""
    directory = artifact(directory, ".temp")
    if directory.exists() and any(directory.iterdir()):
        raise ValueError("Preparation requires an empty scratch directory; choose a new --directory")
    directory.mkdir(parents=True, exist_ok=True)
    hashes = {}
    for source in sorted((ROOT / "src/Mote.Engine").glob("*.cs")):
        data = source.read_bytes()
        hashes[source.name] = hashlib.sha256(data).hexdigest()
        text = data.decode("utf-8-sig").replace("\r\n", "\n")
        text = replace_once(text, "namespace Mote.Engine;", "namespace Probe.Engine;")
        if source.name == "Document.cs":
            changes = [
                ("while ((count = await stream.ReadAsync(bytes, cancellationToken).ConfigureAwait(false)) > 0)\n        {",
                 "while (true)\n        {\n            var readStart = PhaseProbe.Start();\n            count = await stream.ReadAsync(bytes, cancellationToken).ConfigureAwait(false);\n            PhaseProbe.End(0, readStart);\n            if (count <= 0) break;"),
                ("hash.AppendData(bytes, 0, count);", "var hashStart = PhaseProbe.Start();\n            hash.AppendData(bytes, 0, count);\n            PhaseProbe.End(1, hashStart);"),
                ("                decoder.Convert(bytes, consumed,", "                var decodeStart = PhaseProbe.Start();\n                decoder.Convert(bytes, consumed,"),
                ("out var bytesUsed, out var charsUsed, out _);", "out var bytesUsed, out var charsUsed, out _);\n                PhaseProbe.End(2, decodeStart);"),
                ("                AddChunk(chunks, chars, charsUsed, ref length);", "                var chunkStart = PhaseProbe.Start();\n                AddChunk(chunks, chars, charsUsed, ref length);\n                PhaseProbe.End(3, chunkStart);"),
                ("var document = new Document(RopeNode.FromChunks(chunks), path, encoding, markerSize > 0);", "var ropeStart = PhaseProbe.Start();\n        var document = new Document(RopeNode.FromChunks(chunks), path, encoding, markerSize > 0);\n        PhaseProbe.End(4, ropeStart);")]
            for before, after in changes:
                text = replace_once(text, before, after)
        if source.name == "RopeNode.cs":
            before = "    private static int CountBreaks(ReadOnlySpan<char> text)\n    {\n        var count = 0;"
            after = """    private static int CountBreaks(ReadOnlySpan<char> text)
    {
        if (PhaseProbe.DirectBreaks && !text.Contains('\\r')) return text.Count('\\n');
        if (PhaseProbe.FastBreaks && (!PhaseProbe.HybridBreaks || !text.Contains('\\r')))
        {
            // Both experimental variants are rejected; keep them for reproducing the counterexample.
            // A trailing CR counts locally; existing branch metadata corrects cross-leaf CRLF.
            var breaks = text.Count('\\n');
            var start = 0;
            while (start < text.Length)
            {
                var relative = text[start..].IndexOf('\\r');
                if (relative < 0) break;
                var position = start + relative;
                if (position + 1 == text.Length || text[position + 1] != '\\n') breaks++;
                start = position + 1;
            }
            return breaks;
        }
        var count = 0;"""
            text = replace_once(text, before, after)
        (directory / source.name).write_text(text, encoding="utf-8")
    for name in ("Program.cs", "LineCheck.cs"):
        (directory / name).write_text((HERE / (name + ".template")).read_text(), encoding="utf-8")
    relative = __import__("os").path.relpath(ROOT / "src/Mote.Engine/Mote.Engine.csproj", directory).replace("\\", "/")
    (directory / "Probe.csproj").write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><IsAotCompatible>true</IsAotCompatible></PropertyGroup>
  <ItemGroup><ProjectReference Include="{relative}" /></ItemGroup>
</Project>
''')
    metadata = {"source_commit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
                "engine_source_sha256": hashes,
                "template_sha256": {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in HERE.glob("*.template")}}
    metadata["preparation_nonce"] = uuid.uuid4().hex
    fingerprint = hashlib.sha256(json.dumps(metadata, sort_keys=True).encode()).hexdigest()
    metadata["preparation_sha256"] = fingerprint
    (directory / "PreparedIdentity.cs").write_text(
        '/// <summary>Unique preparation identity embedded in the compiled probe.</summary>\n'
        + 'internal static class PreparedIdentity { internal const string Hash = "' + fingerprint + '"; }\n')
    metadata["generated_source_sha256"] = {p.name: hashlib.sha256(p.read_bytes()).hexdigest()
        for p in directory.iterdir() if p.suffix in (".cs", ".csproj")}
    (directory / "prepared.json").write_text(json.dumps(metadata, indent=2))
    print(directory.relative_to(ROOT))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", default=ROOT / ".temp/native-json-open-attribution", type=Path)
    prepare(parser.parse_args().directory)
