$ErrorActionPreference = 'Stop'

& 'C:\Program Files (x86)\dotnet\dotnet.exe' publish `
    'src/PzServerManager.Web' `
    -c Release `
    -r linux-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o 'artifacts/linux-x64'

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
