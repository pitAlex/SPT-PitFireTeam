function Find-AspNetRuntime {
    # PowerShell does not load ASP.NET's shared framework, while the SPT web host does.
    $aspNetCandidates = & dotnet --list-runtimes | ForEach-Object {
        if ($_ -match '^Microsoft.AspNetCore.App (10\.\d+\.\d+) \[(.+)\]$') {
            [pscustomobject]@{ Version = [version]$Matches[1]; Root = $Matches[2] }
        }
    }
    $runtime = $aspNetCandidates | Sort-Object Version -Descending | Select-Object -First 1
    if (!$runtime) { throw 'ASP.NET Core 10 runtime is required to verify the server web components.' }
    Join-Path $runtime.Root $runtime.Version.ToString()
}
