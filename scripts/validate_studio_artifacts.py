#!/usr/bin/env python3
"""Offline schema, notebook and Python syntax validation for a generated Studio project."""
import argparse
import json
from pathlib import Path
import py_compile
import tempfile

from jsonschema import Draft202012Validator
from referencing import Registry, Resource
import nbformat


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=Path, required=True)
    args = parser.parse_args()
    repository = Path(__file__).resolve().parents[1]
    schemas = {}
    registry = Registry()
    for path in (repository / "schemas").glob("*.json"):
        schema = json.loads(path.read_text(encoding="utf-8-sig"))
        Draft202012Validator.check_schema(schema)
        schemas[path.name] = schema
        resource = Resource.from_contents(schema)
        registry = registry.with_resource(path.resolve().as_uri(), resource)
        if "$id" in schema:
            registry = registry.with_resource(schema["$id"], resource)
    for artifact, schema_name in (("project.json", "studio-project.schema.json"),
                                  ("pipeline.json", "pipeline-studio.schema.json")):
        path = args.project / artifact
        if not path.exists():
            continue
        data = json.loads(path.read_text(encoding="utf-8-sig"))
        if artifact == "project.json" and data.get("version") == "1.0.0":
            schema_name = "project-spec.schema.json"
        Draft202012Validator(schemas[schema_name], registry=registry).validate(data)
    json_count = python_count = notebook_count = 0
    with tempfile.TemporaryDirectory(prefix="forge-syntax-") as temporary:
        for path in sorted(args.project.rglob("*")):
            if not path.is_file():
                continue
            if path.suffix == ".json":
                json.loads(path.read_text(encoding="utf-8-sig"))
                json_count += 1
            if path.suffix == ".py":
                py_compile.compile(str(path), cfile=str(Path(temporary) / f"{python_count}.pyc"), doraise=True)
                python_count += 1
            if path.suffix == ".ipynb":
                nbformat.validate(nbformat.read(path, as_version=4))
                notebook_count += 1
    print(json.dumps({"status": "static-validated", "jsonFiles": json_count,
                      "pythonFiles": python_count, "notebooks": notebook_count,
                      "cloudExecuted": False}))


if __name__ == "__main__":
    main()
