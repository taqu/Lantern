
# Check master key
$masterKey = $env:MEILI_MASTER_KEY
if ([string]::IsNullOrWhiteSpace($masterKey)) {
    Write-Error @"
MEILI_MASTER_KEY environment variable is not set.

Set it before calling this script:
    `$env:MEILI_MASTER_KEY = "your-development-key-here"

Do not use a weak or predictable key even in development.
"@
    exit 1
}

$meiliUrl = "http://localhost:7700"
$indexUid = "lantern-documents"

$settings = @{
  "lantern-granite" = @{
    "source" = "rest"
    "url" = "http://127.0.0.1:7701/embed"
    "dimensions" = 384
    "request" = @{
      "text" = "{{text}}"
    }
    "response" = @{
      "embedding" = "{{embedding}}"
    }
    "documentTemplate" = "{{doc.title}}\n{{doc.sectionTitle}}\n{{doc.content}}"
    "documentTemplateMaxBytes" = 8192
  }
}

$jsonBody = $settings | ConvertTo-Json -Depth 10

$headers = @{
    "Authorization" = "Bearer $env:MEILI_MASTER_KEY"
    "Content-Type"  = "application/json"
}

try {
    $uri = "$meiliUrl/indexes/$indexUid/settings/embedders"
    $response = Invoke-RestMethod -Uri $uri -Method Patch -Headers $headers -Body $jsonBody
    
    Write-Host "request succeeded" -ForegroundColor Green
    $response | Format-List
}
catch {
    Write-Error "error occurs: $_"
}

