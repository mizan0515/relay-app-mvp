#!/usr/bin/env sh
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)

if [ "$#" -eq 0 ]; then
  exec "$script_dir/_ps1_runner.sh" "$script_dir/Validate-TemplateVariants.ps1" -RunVariantValidators
fi

exec "$script_dir/_ps1_runner.sh" "$script_dir/Validate-TemplateVariants.ps1" "$@"
