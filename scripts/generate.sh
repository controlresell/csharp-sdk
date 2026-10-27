#!/usr/bin/env bash
# Regenerates the client from the OpenAPI document of the ControlResell public API.
#
#   scripts/generate.sh [spec]   # a file or a URL; default: https://api.controlresell.com/api/public/openapi.json
#
# NSwag (version pinned in .config/dotnet-tools.json) writes one file, src/ControlResell.Sdk/Generated/Api.g.cs, and the
# script writes .spec-sha256. The document itself is not committed.
#
# Options worth knowing:
#   - /excludedParameterNames:Idempotency-Key  the Idempotency-Key header is set by ControlResellHandler (the same key on every
#     retry of a call), not asked of the caller on each write;
#   - /generateOptionalParameters:true         optional query parameters are C# optional arguments (ListItemsAsync(sold: false));
#   - NSwag reads the OpenAPI 3.1 nullable reference `anyOf: [{$ref: X}, {type: "null"}]` as an empty object type
#     (`EstimatedPrice`, `PurchasePrice`... instead of `Money?`); the script rewrites it to `allOf: [{$ref: X}], nullable: true`
#     before generating;
#   - operations are grouped by tag, named by operationId: ItemsApi.ListItemsAsync, IItemsApi.
set -euo pipefail

SPEC="${1:-https://api.controlresell.com/api/public/openapi.json}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

case "$SPEC" in
  http://*|https://*) curl -fsSL -o "$WORK/openapi.json" "$SPEC" ;;
  *) cp "$SPEC" "$WORK/openapi.json" ;;
esac

# NSwag workaround: a nullable $ref (OpenAPI 3.1 anyOf with null) becomes allOf + nullable.
python3 -I - "$WORK/openapi.json" <<'PY'
import json, sys

def fix(node):
    if isinstance(node, dict):
        any_of = node.get("anyOf")
        if isinstance(any_of, list) and len(any_of) == 2:
            refs = [x for x in any_of if isinstance(x, dict) and "$ref" in x]
            nulls = [x for x in any_of if isinstance(x, dict) and x.get("type") == "null"]
            if len(refs) == 1 and len(nulls) == 1:
                del node["anyOf"]
                node["allOf"] = [refs[0]]
                node["nullable"] = True
        for value in node.values():
            fix(value)
    elif isinstance(node, list):
        for value in node:
            fix(value)

path = sys.argv[1]
with open(path) as f:
    spec = json.load(f)
fix(spec)
with open(path.replace("openapi.json", "openapi.nswag.json"), "w") as f:
    json.dump(spec, f)
PY

cd "$ROOT"
dotnet tool restore > /dev/null
dotnet nswag openapi2csclient \
  /input:"$WORK/openapi.nswag.json" \
  /output:"$WORK/Api.g.cs" \
  /namespace:ControlResell.Sdk \
  '/className:{controller}Api' \
  /operationGenerationMode:MultipleClientsFromFirstTagAndOperationId \
  /jsonLibrary:SystemTextJson \
  /generateClientInterfaces:true \
  /generateOptionalParameters:true \
  /generateNullableReferenceTypes:true \
  /useBaseUrl:false \
  /excludedParameterNames:Idempotency-Key \
  > "$WORK/generate.log"

mkdir -p src/ControlResell.Sdk/Generated
cp "$WORK/Api.g.cs" src/ControlResell.Sdk/Generated/Api.g.cs

shasum -a 256 "$WORK/openapi.json" | cut -d' ' -f1 > .spec-sha256
echo "Generated from $SPEC (sha256 $(cat .spec-sha256))"
