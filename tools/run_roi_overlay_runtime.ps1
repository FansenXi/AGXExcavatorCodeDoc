param(
  [string]$Config = "tools/roi_runtime_config.yaml",
  [switch]$SelfTest
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$venvRoot = Join-Path $repoRoot ".venv-roi"
$venvPython = Join-Path $venvRoot "Scripts\\python.exe"
$requirements = Join-Path $repoRoot "tools\\requirements_roi_runtime.txt"
$entryScript = Join-Path $repoRoot "tools\\roi_overlay_runtime.py"
$configPath = Join-Path $repoRoot $Config

function Resolve-BasePython {
  if (Get-Command py -ErrorAction SilentlyContinue) {
    return "py"
  }

  if (Get-Command python -ErrorAction SilentlyContinue) {
    return "python"
  }

  throw "Neither 'py' nor 'python' is available on PATH."
}

function Ensure-Venv {
  if (Test-Path $venvPython) {
    return
  }

  $basePython = Resolve-BasePython
  if ($basePython -eq "py") {
    & py -3.12 -m venv $venvRoot
  }
  else {
    & python -m venv $venvRoot
  }
}

function Ensure-Dependencies {
  & $venvPython -c "import cv2, yaml, imageio_ffmpeg, onnxruntime" *> $null
  if ($LASTEXITCODE -eq 0) {
    return
  }

  & $venvPython -m pip install --upgrade pip
  & $venvPython -m pip install -r $requirements
}

Ensure-Venv
Ensure-Dependencies

$arguments = @($entryScript, "--config", $configPath)
if ($SelfTest) {
  $arguments += "--self-test"
}

& $venvPython @arguments
exit $LASTEXITCODE
