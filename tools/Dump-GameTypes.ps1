<#
.SYNOPSIS
    Lists methods, fields and enum members of types in a game or mod assembly.

.DESCRIPTION
    Reads assembly metadata directly, so it needs no decompiler and never loads or runs
    the assembly. Use this for quick lookups - "does CharacterItems still have Equip?",
    "what is in the STATUSTYPE enum now?" - and reach for ilspycmd when you need bodies:

        dotnet ilspycmd <assembly.dll> -r "<PEAK>\PEAK_Data\Managed" -o <outdir>

.EXAMPLE
    ./tools/Dump-GameTypes.ps1 -Types CharacterItems,ItemCooking

.EXAMPLE
    ./tools/Dump-GameTypes.ps1 -Types STATUSTYPE -Members Fields

.EXAMPLE
    ./tools/Dump-GameTypes.ps1 -TypePattern '^Action_'
#>
param(
    [string] $Dll = 'C:\Games\Steam\steamapps\common\PEAK\PEAK_Data\Managed\Assembly-CSharp.dll',

    # Exact type names to dump members for (nested types are matched on their short name).
    [string[]] $Types = @(),

    # Regex over type names; lists matching type names instead of dumping members.
    [string] $TypePattern = '',

    [ValidateSet('Methods', 'Fields', 'All')]
    [string] $Members = 'All'
)

if (-not (Test-Path $Dll)) {
    throw "Assembly not found: $Dll. Pass -Dll with the path to your PEAK install."
}

$stream = [System.IO.File]::OpenRead($Dll)
$peReader = New-Object System.Reflection.PortableExecutable.PEReader($stream)

try {
    $md = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($peReader)

    foreach ($handle in $md.TypeDefinitions) {
        $type = $md.GetTypeDefinition($handle)
        $name = $md.GetString($type.Name)
        $ns = $md.GetString($type.Namespace)
        $full = if ($ns) { "$ns.$name" } else { $name }

        if ($TypePattern) {
            if ($name -match $TypePattern) { Write-Output $full }
            continue
        }

        if ($Types -notcontains $name) { continue }

        Write-Output ''
        Write-Output "=== $full ==="

        if ($Members -in 'Fields', 'All') {
            foreach ($fh in $type.GetFields()) {
                $field = $md.GetFieldDefinition($fh)
                $fieldName = $md.GetString($field.Name)
                # Enums carry a synthetic value__ field that is never interesting.
                if ($fieldName -ne 'value__') { Write-Output "  field  $fieldName" }
            }
        }

        if ($Members -in 'Methods', 'All') {
            foreach ($mh in $type.GetMethods()) {
                $method = $md.GetMethodDefinition($mh)
                $attrs = $method.Attributes
                $visibility =
                    if ($attrs.HasFlag([System.Reflection.MethodAttributes]::Public)) { 'pub ' }
                    elseif ($attrs.HasFlag([System.Reflection.MethodAttributes]::Private)) { 'priv' }
                    else { 'int ' }
                Write-Output "  $visibility   $($md.GetString($method.Name))"
            }
        }
    }
}
finally {
    $peReader.Dispose()
    $stream.Dispose()
}
