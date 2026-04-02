#!/usr/bin/env bash
set -euo pipefail

TEMPLATE_ID="${1:-simulated-uav}"
TEMPLATES_DIR="${2:-../templates}"
TARGET="$TEMPLATES_DIR/$TEMPLATE_ID"

dotnet build -c Release -p:TemplateId="$TEMPLATE_ID" -p:TemplatesDir="$(realpath "$TEMPLATES_DIR")"

echo "Deployed '$TEMPLATE_ID' to '$TARGET'"
