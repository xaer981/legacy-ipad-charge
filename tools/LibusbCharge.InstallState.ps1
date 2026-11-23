$libusbDriverPath = Join-Path $env:windir 'System32\drivers\libusb0.sys'
$libusbJournalPath = Join-Path $env:windir 'System32\drivers\LegacyIpadCharge-libusb.clixml'
$libusbDriverHash = 'CDA0070FE4F7C5DE5D3BB06FA6D7E29E6F6740C0F2FA319FB5C1AF235D78D616'
$libusbInfHash = '62ABAB852A7FE61CFC9490A57908ED2793F3681575FABFB52987FEB44FD51AA3'
$libusbToolHash = '95F4F575639B9A4BF4F8A2F2EE226720CF87FB962AA33B6327EF0C704B63A3C4'

function Enter-LibusbInstallLock {
    $path = [IO.Path]::ChangeExtension($libusbJournalPath, 'lock')
    [IO.FileStream]::new($path, 'OpenOrCreate', 'ReadWrite', 'None', 1, 'DeleteOnClose')
}

function Get-LibusbPackages {
    Get-WindowsDriver -Online | Where-Object {
        [IO.Path]::GetFileName($_.OriginalFileName) -eq 'libusb0.inf'
    }
}

function Save-LibusbInstallJournal($Installation) {
    $temporary = Join-Path ([IO.Path]::GetDirectoryName($libusbJournalPath)) (
        'LegacyIpadCharge-libusb-' + [guid]::NewGuid().ToString('N') + '.tmp'
    )
    try {
        $Installation | Export-Clixml -LiteralPath $temporary
        if ([IO.File]::Exists($libusbJournalPath)) {
            [IO.File]::Replace($temporary, $libusbJournalPath, [System.Management.Automation.Language.NullString]::Value)
        } else {
            [IO.File]::Move($temporary, $libusbJournalPath)
        }
    } finally {
        [IO.File]::Delete($temporary)
    }
}
