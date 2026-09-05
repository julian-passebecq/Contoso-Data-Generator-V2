"""Neutral typed recipes compiled to DuckDB SQL or Polars expressions; no user code or paths."""
import re
from common import read, write, sha, identifier as ident, literal, now

OPS = {"source", "select", "rename", "cast", "derive", "filter", "join", "lookup", "conditional-split", "aggregate", "deduplicate", "window", "sink"}
KEYS = {"id", "op", "entity", "input", "right", "columns", "renames", "casts", "name", "field", "function", "expression", "predicate", "on", "how", "matched", "groupBy", "orderBy", "measures"}


def name(value):
    if not isinstance(value, str) or not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]{0,63}", value): raise ValueError("Unsafe recipe identifier")
    return value


def validate(recipe):
    if set(recipe) - {"recipeVersion", "maxRows", "steps"} or recipe.get("recipeVersion") != 1 or not 1 <= recipe.get("maxRows", 100000) <= 1000000:
        raise ValueError("Invalid recipe header/budget")
    steps = recipe["steps"]
    if not isinstance(steps, list) or not 1 <= len(steps) <= 100: raise ValueError("Recipe needs 1..100 steps")
    ids = set()
    for step in steps:
        if set(step) - KEYS or step.get("op") not in OPS or name(step["id"]) in ids: raise ValueError("Invalid/duplicate recipe step or unsupported operator")
        ids.add(step["id"])
        name(step.get("entity") if step["op"] == "source" else step.get("input"))
        if step["op"] in ("join", "lookup"): name(step.get("right"))
    ordered, done = [], set()
    while len(done) < len(steps):
        ready = sorted((s for s in steps if s["id"] not in done and {s[k] for k in ("input", "right") if s.get(k)} <= done), key=lambda s: s["id"])
        if not ready: raise ValueError("Recipe cycle or missing dependency")
        ordered.extend(ready); done.update(s["id"] for s in ready)
    return ordered


def expression(node, columns, engine, depth=0):
    if depth > 12 or not isinstance(node, dict) or set(node) - {"field", "value", "operator", "args"}: raise ValueError("Invalid recipe expression")
    import polars as pl
    operator = node.get("operator")
    if operator is None:
        if set(node) == {"field"}:
            key = name(node["field"])
            if key not in columns: raise ValueError("Unknown recipe field: " + key)
            return ident(key) if engine == "duckdb" else pl.col(key)
        if set(node) != {"value"}: raise ValueError("Expression requires a field or scalar literal")
        value = node["value"]
        if not isinstance(value, (str, int, float, bool, type(None))) or len(str(value)) > 1024: raise ValueError("Invalid literal")
        if engine == "polars": return pl.lit(value)
        if value is None: return "NULL"
        if isinstance(value, bool): return "TRUE" if value else "FALSE"
        if isinstance(value, str): return literal(value)
        import math
        if not math.isfinite(value): raise ValueError("Nonfinite literal")
        return str(value)
    allowed = {"+", "-", "*", "/", "==", "!=", ">", ">=", "<", "<=", "and", "or", "not", "is-null", "coalesce"}
    args = node.get("args", [])
    if set(node) != {"operator", "args"} or operator not in allowed or len(args) != (1 if operator in ("not", "is-null") else 2): raise ValueError("Unsupported operator or arity")
    values = [expression(a, columns, engine, depth + 1) for a in args]
    a = values[0]
    if engine == "duckdb":
        if operator == "not": return f"(NOT {a})"
        if operator == "is-null": return f"({a} IS NULL)"
        b = values[1]
        if operator == "coalesce": return f"coalesce({a}, {b})"
        return f"({a} {'=' if operator == '==' else operator} {b})"
    if operator == "not": return ~a
    if operator == "is-null": return a.is_null()
    b = values[1]
    if operator == "coalesce": return pl.coalesce(a, b)
    return {"+": lambda: a+b, "-": lambda: a-b, "*": lambda: a*b, "/": lambda: a/b, "==": lambda: a==b, "!=": lambda: a!=b,
        ">": lambda: a>b, ">=": lambda: a>=b, "<": lambda: a<b, "<=": lambda: a<=b, "and": lambda: a & b, "or": lambda: a | b}[operator]()


def fields(values, columns, required=False):
    if not isinstance(values, list) or (required and not values) or len(values) != len(set(values)): raise ValueError("Expected distinct recipe fields")
    for key in values:
        if name(key) not in columns: raise ValueError("Unknown recipe field: " + key)
    return values


def execute(root, state, recipe, engine):
    if engine not in ("duckdb", "polars"): raise ValueError("Wrangling supports DuckDB/Polars; other engines are explicit unsupported mappings")
    import duckdb
    import polars as pl
    ordered = validate(recipe)
    limit = recipe.get("maxRows", 100000)
    frames, outputs, compiled = {}, {}, []
    directory = state / "wrangling"
    directory.mkdir()
    governed = read(state / "silver_contract.json")["tables"]
    with duckdb.connect() as db:
        for step in ordered:
            op, key = step["op"], step["id"]
            if op == "source":
                entity = step["entity"]
                if entity not in governed: raise ValueError("Recipe source must name a governed Silver entity")
                source = state / "lake/silver" / entity / "*.parquet"
                sql = "SELECT * FROM read_parquet(" + literal(source.as_posix()) + ")"
                frame = pl.scan_parquet(source) if engine == "polars" else None
                columns = []
            else:
                parent = step["input"]
                columns = frames[parent] if engine == "duckdb" else frames[parent].collect_schema().names()
                sql = "SELECT * FROM " + ident(parent)
                frame = frames[parent] if engine == "polars" else None
                if op == "select":
                    chosen = fields(step.get("columns"), columns, True)
                    sql = "SELECT " + ", ".join(map(ident, chosen)) + " FROM " + ident(parent)
                    if frame is not None: frame = frame.select(chosen)
                elif op == "rename":
                    mapping = step.get("renames", {})
                    if not mapping: raise ValueError("rename requires a mapping")
                    fields(list(mapping), columns, True)
                    names = [name(mapping.get(c, c)) for c in columns]
                    if len(set(names)) != len(names): raise ValueError("Rename collision")
                    sql = "SELECT " + ", ".join(ident(c) + " AS " + ident(mapping.get(c, c)) for c in columns) + " FROM " + ident(parent)
                    if frame is not None: frame = frame.rename(mapping)
                elif op == "cast":
                    mapping = step.get("casts", {})
                    fields(list(mapping), columns, True)
                    types = {"int64": ("BIGINT", pl.Int64), "float64": ("DOUBLE", pl.Float64), "string": ("VARCHAR", pl.String), "boolean": ("BOOLEAN", pl.Boolean)}
                    if any(t not in types for t in mapping.values()): raise ValueError("Unsupported cast")
                    sql = "SELECT " + ", ".join(f"CAST({ident(c)} AS {types[mapping[c]][0]}) AS {ident(c)}" if c in mapping else ident(c) for c in columns) + " FROM " + ident(parent)
                    if frame is not None: frame = frame.with_columns([pl.col(c).cast(types[t][1], strict=True) for c, t in mapping.items()])
                elif op == "derive":
                    target = name(step["name"])
                    if target in columns: raise ValueError("derive cannot replace a governed field")
                    value = expression(step["expression"], columns, engine)
                    if frame is not None: frame = frame.with_columns(value.alias(target))
                    else: sql = f"SELECT *, {value} AS {ident(target)} FROM {ident(parent)}"
                elif op in ("filter", "conditional-split"):
                    predicate = expression(step["predicate"], columns, engine)
                    matched = step.get("matched", True)
                    if type(matched) is not bool: raise ValueError("matched must be boolean")
                    if frame is not None:
                        predicate = predicate.fill_null(False)
                        frame = frame.filter(predicate if matched else ~predicate)
                    else: sql += " WHERE " + ("" if matched else "NOT ") + f"coalesce(({predicate}), false)"
                elif op in ("join", "lookup"):
                    right = step["right"]
                    right_columns = frames[right] if engine == "duckdb" else frames[right].collect_schema().names()
                    on = fields(step.get("on"), columns, True); fields(on, right_columns, True)
                    how = step.get("how", "inner")
                    if how not in ("inner", "left"): raise ValueError("Unsupported join kind")
                    if (set(columns) & set(right_columns)) - set(on): raise ValueError("Rename overlapping non-key join columns explicitly")
                    if op == "lookup":
                        if engine == "duckdb": duplicates = db.execute(f"SELECT count(*) FROM (SELECT {', '.join(map(ident, on))} FROM {ident(right)} GROUP BY ALL HAVING count(*)>1)").fetchone()[0]
                        else: duplicates = frames[right].group_by(on).len().filter(pl.col("len") > 1).collect().height
                        if duplicates: raise ValueError("Lookup keys must be unique")
                    sql += f" {how.upper()} JOIN {ident(right)} USING ({', '.join(map(ident, on))})"
                    if frame is not None: frame = frame.join(frames[right], on=on, how=how, coalesce=True, nulls_equal=False)
                elif op == "aggregate":
                    groups = fields(step.get("groupBy", []), columns)
                    measures, expressions, selects = step.get("measures", []), [], []
                    if not measures: raise ValueError("aggregate requires measures")
                    for measure in measures:
                        if set(measure) - {"name", "function", "field"}: raise ValueError("Invalid measure")
                        label, function = name(measure["name"]), measure["function"]
                        if function not in ("count", "sum", "mean", "min", "max"): raise ValueError("Unsupported aggregate")
                        field = measure.get("field")
                        if function != "count": fields([field], columns, True)
                        selects.append(("count(*)" if function == "count" else f"{'avg' if function == 'mean' else function}({ident(field)})") + " AS " + ident(label))
                        expressions.append((pl.len().cast(pl.Int64) if function == "count" else getattr(pl.col(field), function)()).alias(label))
                    labels = groups + [m["name"] for m in measures]
                    if len(labels) != len(set(labels)): raise ValueError("Aggregate output name collision")
                    sql = "SELECT " + ", ".join([*map(ident, groups), *selects]) + " FROM " + ident(parent) + (" GROUP BY " + ", ".join(map(ident, groups)) if groups else "")
                    if frame is not None: frame = frame.group_by(groups).agg(expressions) if groups else frame.select(expressions)
                elif op == "deduplicate":
                    keys = fields(step.get("columns", columns), columns, True)
                    order = fields(step.get("orderBy", []), columns)
                    order += sorted(set(columns) - set(order))
                    sql += f" QUALIFY row_number() OVER (PARTITION BY {', '.join(map(ident, keys))} ORDER BY {', '.join(map(ident, order))} NULLS LAST)=1"
                    if frame is not None: frame = frame.sort(order, nulls_last=True).unique(keys, keep="first", maintain_order=True)
                elif op == "window":
                    target = name(step["name"])
                    if target in columns: raise ValueError("Window output collision")
                    groups = fields(step.get("groupBy", []), columns)
                    order = fields(step.get("orderBy", []), columns, True)
                    order += sorted(set(columns) - set(order))
                    function = step.get("function")
                    if function not in ("row_number", "sum"): raise ValueError("Unsupported window function")
                    if function == "sum": fields([step.get("field")], columns, True)
                    value = "row_number()" if function == "row_number" else f"sum({ident(step['field'])})"
                    over = ("PARTITION BY " + ", ".join(map(ident, groups)) + " " if groups else "") + "ORDER BY " + ", ".join(ident(c) + " NULLS LAST" for c in order) + " ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW"
                    sql = f"SELECT *, {value} OVER ({over}) AS {ident(target)} FROM {ident(parent)}"
                    if frame is not None:
                        frame = frame.sort(order, nulls_last=True)
                        value = pl.int_range(1, pl.len()+1, dtype=pl.Int64) if function == "row_number" else pl.col(step["field"]).cum_sum()
                        frame = frame.with_columns((value.over(groups) if groups else value).alias(target))
                elif op != "sink": raise ValueError("Unsupported operator")
            if engine == "duckdb":
                count = db.execute("SELECT count(*) FROM (" + sql + ")").fetchone()[0]
                if count > limit: raise ValueError(f"Recipe materialization would exceed maxRows: {count}>{limit}")
                db.execute(f"CREATE TABLE {ident(key)} AS {sql}")
                frames[key] = [c[0] for c in db.execute("SELECT * FROM " + ident(key) + " LIMIT 0").description]
                compiled.append(sql)
            else:
                count = frame.select(pl.len()).collect(engine="streaming").item()
                if count > limit: raise ValueError(f"Recipe materialization would exceed maxRows: {count}>{limit}")
                compiled.append(frame.explain(engine="streaming"))
                frames[key] = frame.collect(engine="streaming").lazy()
            if op == "sink" or step is ordered[-1]:
                path = directory / (key + ".parquet")
                if engine == "duckdb": db.execute(f"COPY (SELECT * FROM {ident(key)} ORDER BY ALL) TO ? (FORMAT PARQUET)", [str(path)])
                else: frames[key].collect().write_parquet(path)
                outputs[key] = {"rows": count, "sha256": sha(path)}
    (directory / ("compiled.sql" if engine == "duckdb" else "polars_plans.txt")).write_text("\n\n".join(compiled), encoding="utf-8")
    result = {"status": "executed", "engine": engine, "outputs": outputs, "maxRows": limit, "completedAt": now(),
        "scope": "Recipe outputs are separate derived assets; canonical Silver/Gold/truth are preserved."}
    write(directory / "recipe_execution.json", result)
    return result
