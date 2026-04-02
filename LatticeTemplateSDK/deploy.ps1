param(
    [string]$TemplateId = "simulated-uav",
    [string]$TemplatesDir = "..\templates"
)

$resolvedTemplatesDir = Resolve-Path $TemplatesDir -ErrorAction SilentlyContinue
if (-not $resolvedTemplatesDir) {
    $resolvedTemplatesDir = $TemplatesDir
}

dotnet build -c Release "-p:TemplateId=$TemplateId" "-p:TemplatesDir=$resolvedTemplatesDir"

Write-Host "Deployed '$TemplateId' to '$resolvedTemplatesDir\$TemplateId'"
