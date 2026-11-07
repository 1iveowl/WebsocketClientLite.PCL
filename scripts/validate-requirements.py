#!/usr/bin/env python3
"""Validate the v1 Close manifest and resolve local evidence references (stdlib only)."""
import argparse
import json
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
SCHEMA = ROOT / 'verification/requirements.schema.json'


class Invalid(ValueError):
    pass


def require(condition, message):
    if not condition:
        raise Invalid(message)


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, f'duplicate JSON key: {key}')
        result[key] = value
    return result


def read_json(path):
    return json.loads(path.read_text(), object_pairs_hook=unique_object)


def shape(value, schema, where='$'):
    """Implement only the JSON Schema keywords used by our checked-in v1 schema.

    Unknown keywords fail closed; this is not a general JSON Schema engine.
    """
    supported = {'$schema', 'title', 'type', 'properties', 'required',
                 'additionalProperties', 'items', 'minItems', 'uniqueItems',
                 'minLength', 'pattern', 'enum', 'const'}
    require(not set(schema) - supported, f'{where}: unsupported schema keyword')
    if 'const' in schema:
        require(type(value) is type(schema['const']) and value == schema['const'],
                f'{where}: unsupported value/version')
    if 'enum' in schema:
        require(value in schema['enum'], f'{where}: invalid choice {value!r}')
    if 'type' in schema:
        types = {'object': dict, 'array': list, 'string': str}
        require(schema['type'] in types, f'{where}: unsupported schema type')
        require(type(value) is types[schema['type']], f'{where}: expected {schema["type"]}')
    if isinstance(value, dict):
        require(not set(schema.get('required', [])) - value.keys(), f'{where}: missing required fields')
        props = schema.get('properties', {})
        if schema.get('additionalProperties') is False:
            require(not value.keys() - props.keys(), f'{where}: unknown fields {value.keys() - props.keys()}')
        for key, item in value.items():
            if key in props:
                shape(item, props[key], f'{where}.{key}')
    if isinstance(value, list):
        require(len(value) >= schema.get('minItems', 0), f'{where}: too few entries')
        if schema.get('uniqueItems'):
            require(len({json.dumps(v, sort_keys=True) for v in value}) == len(value), f'{where}: duplicate entries')
        for index, item in enumerate(value):
            shape(item, schema['items'], f'{where}[{index}]')
    if isinstance(value, str):
        require(len(value) >= schema.get('minLength', 0), f'{where}: empty string')
        if 'pattern' in schema:
            require(re.search(schema['pattern'], value), f'{where}: invalid string {value!r}')


def source(root, name):
    path = (root / name).resolve()
    require(not Path(name).is_absolute() and path.is_relative_to(root.resolve()),
            f'path outside repository: {name}')
    require(path.is_file(), f'missing file: {name}')
    return path.read_text()


def masked(text):
    # Preserve positions/newlines while excluding comments and ordinary/verbatim
    # strings and character literals from declaration and brace searches.
    return re.sub(r'//[^\n]*|/\*[\s\S]*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'',
                  lambda m: re.sub(r'[^\n]', ' ', m[0]), text)


def body(text, opening):
    require(opening >= 0, 'missing declaration body')
    depth = 0
    for i in range(opening, len(text)):
        if text[i] == '{':
            depth += 1
        elif text[i] == '}':
            depth -= 1
            if depth == 0:
                return text[opening + 1:i]
    raise Invalid('unbalanced declaration body')


def one(matches, label):
    matches = list(matches)
    require(len(matches) == 1, f'unresolved or ambiguous {label} ({len(matches)} declarations)')
    return matches[0]


def resolve(root, ref, test=False):
    label = f'{ref["file"]}:{ref["symbol"]}'
    text = masked(source(root, ref['file']))
    parts = ref['symbol'].split('.')
    require(len(parts) >= 3, f'invalid qualified symbol: {label}')
    owner, method = parts[-2:]
    namespace = '.'.join(parts[:-2])
    if ref['file'].endswith('.dfy'):
        module = one(re.finditer(r'\bmodule\s+(?:\{:[^}]*\}\s*)?' + re.escape(namespace) + r'\s*\{', text), label)
        scope = body(text, module.end() - 1)
        prefix = r'^\s*static\s+method\s+'
    else:
        require(ref['file'].endswith('.cs'), f'unsupported source type: {label}')
        one(re.finditer(r'^namespace\s+' + re.escape(namespace) + r'\s*;', text, re.M), label)
        scope = text
        prefix = r'^\s*(?:public|internal|private|protected)\s+(?:(?:static|async|virtual|override)\s+)*[\w<>?,\[\].]+\s+'
    cls = one(re.finditer(r'\bclass\s+' + re.escape(owner) + r'\b', scope), label)
    scope = body(scope, scope.find('{', cls.end()))
    declaration = one(re.finditer(prefix + re.escape(method) + r'\s*\(', scope, re.M), label)
    # Reject a nested class method being attributed to the enclosing class.
    before = scope[:declaration.start()]
    require(before.count('{') == before.count('}'), f'nested declaration: {label}')
    if test:
        require(re.search(r'\[(?:Fact|Theory)\](?:\s*\[[^\]]+\])*\s*$', before),
                f'not an xUnit test declaration: {label}')
    return scope[declaration.start():scope.find('{', declaration.end())]


def validate(manifest, root=ROOT):
    shape(manifest, read_json(SCHEMA))
    boundaries = {}
    review = source(root, 'verification/close-intent-review.md')
    for entry in manifest['boundary_seeds']:
        require(entry['id'] not in boundaries, f'duplicate boundary ID: {entry["id"]}')
        boundaries[entry['id']] = entry
        require(review.count(f'<a id="{entry["review_anchor"]}"></a>') == 1,
                f'unresolved boundary anchor: {entry["review_anchor"]}')
    ids, obligations = set(), set()
    for req in manifest['requirements']:
        id = req['id']
        require(id not in ids, f'duplicate requirement ID: {id}')
        ids.add(id)
        require(f'### {id}\n' in review, f'missing intent review: {id}')
        require(set(req['assumptions']) <= boundaries.keys(), f'{id}: dangling boundary reference')
        for contract in req['contracts']:
            header = resolve(root, contract)
            normalized = [' '.join(line.split()) for line in header.splitlines()]
            for clause in contract['clauses']:
                require(clause.startswith(('requires ', 'ensures ')) and clause in normalized,
                        f'{id}: unresolved contract clause: {clause}')
        for path in req['production_paths']:
            resolve(root, path['reference'])
        kinds = set()
        for obligation in req['obligations']:
            oid, kind = obligation['id'], obligation['kind']
            require(oid not in obligations, f'duplicate obligation ID: {oid}')
            obligations.add(oid)
            kinds.add(kind)
            refs = obligation['references']
            if obligation['delivery'] == 'existing' and kind in ('proof', 'test'):
                require(refs, f'{oid}: existing evidence requires references')
            if kind == 'proof':
                require(req['contracts'], f'{oid}: proof requires a contract')
                require(refs and all(any(r['file'] == c['file'] and r['symbol'] == c['symbol']
                                        for c in req['contracts']) for r in refs),
                        f'{oid}: proof reference must match a contract')
            for ref in refs:
                resolve(root, ref, test=kind == 'test')
        require('review' in kinds and ('proof' in kinds or 'test' in kinds), f'{id}: incomplete evidence obligations')
        require(not req['contracts'] or 'proof' in kinds, f'{id}: contract without proof obligation')
    return len(ids)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('manifest', nargs='?', type=Path, default=ROOT / 'verification/requirements.json')
    args = parser.parse_args()
    try:
        count = validate(read_json(args.manifest))
    except (Invalid, OSError, ValueError) as error:
        print(f'requirements: {error}', file=sys.stderr)
        return 1
    print(f'Validated {count} requirements (structure and references only; no evidence executed).')
    return 0


if __name__ == '__main__':
    sys.exit(main())
