# Exercise the close decisions with a stand-in Office document, without loading Office or user data.
param([string]$AssemblyPath = '')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -ReferencedAssemblies System.Windows.Forms @'
using System;
using System.Windows.Forms;
public sealed class DocumentCloseFixture
{
    public bool Saved { get; set; }
    public bool RejectSave { get; set; }
    public int Saves { get; private set; }
    public void Save()
    {
        Saves++;
        if (RejectSave) throw new InvalidOperationException("Fixture save rejected");
        Saved = true;
    }
    public static Func<DialogResult> Cancel { get { return () => DialogResult.Cancel; } }
    public static Func<DialogResult> AcceptSave { get { return () => DialogResult.Yes; } }
    public static Func<string, bool> KeepTab { get { return path => false; } }
}
'@
$root = Split-Path -Parent $PSScriptRoot
if (-not $AssemblyPath) { $AssemblyPath = Join-Path $root 'build-test\StickyNotesPro.test.exe' }
$assembly = [Reflection.Assembly]::LoadFile([IO.Path]::GetFullPath($AssemblyPath))
$private = [Reflection.BindingFlags]'Instance,NonPublic'
function Assert($value, [string]$message) { if (-not $value) { throw $message } }
$edit = [Activator]::CreateInstance($assembly.GetType('StickyPro.OfficeEdit', $true), $true)
$tabs = [Activator]::CreateInstance($assembly.GetType('StickyPro.EditorTabs', $true), $true)
$page = [Activator]::CreateInstance($assembly.GetType('StickyPro.DocumentsPage', $true), $true)
$main = [Activator]::CreateInstance($assembly.GetType('StickyPro.MainForm', $true), $true)
try {
    $doc = New-Object DocumentCloseFixture
    $field = $edit.GetType().GetField('doc', $private)
    $field.SetValue($edit, $doc)
    $confirm = $edit.GetType().GetMethod('ConfirmClose', $private)
    Assert (-not $confirm.Invoke($edit, @([DocumentCloseFixture]::Cancel))) 'Cancel approved closing a dirty editor.'
    Assert ($doc.Saves -eq 0) 'Cancel attempted to save.'
    $doc.RejectSave = $true
    Assert (-not $confirm.Invoke($edit, @([DocumentCloseFixture]::AcceptSave))) 'A failed save approved closing the editor.'
    Assert ($edit.Dirty -and $edit.Error -like '*Fixture save rejected*') 'A rejected save lost its dirty state or error.'
    $doc.RejectSave = $false
    Assert ($confirm.Invoke($edit, @([DocumentCloseFixture]::AcceptSave))) 'A successful save did not approve closing.'
    Assert (-not $edit.Dirty) 'A successful save did not clear the dirty state.'

    $tabs.Show('First.docx'); $tabs.Show('Second.docx'); $tabs.CanClose = [DocumentCloseFixture]::KeepTab
    $tabs.CloseActive()
    Assert ($tabs.Paths.Count -eq 2 -and $tabs.Active -eq 'Second.docx') 'A veto removed an active tab.'
    $tabs.CanClose = $null; $tabs.CloseActive()
    Assert ($tabs.Paths.Count -eq 1) 'An allowed close left the tab in the list.'

    $save = $page.GetType().GetField('saveBtn', $private).GetValue($page)
    Assert (-not $save.Enabled) 'Save was enabled on the empty Documents page.'
    $actions = $page.GetType().GetField('documentActions', $private).GetValue($page)
    Assert (@($actions | Where-Object Enabled).Count -eq 0) 'Empty Documents page has enabled file actions.'
    $main.ShowPage('notes:unknown')
    Assert ($main.Page -eq 'dashboard') 'An invalid page suffix blanked the main window.'
    $main.ShowPage('system:uninstall')
    $systemPage = $main.GetType().GetField('system', $private).GetValue($main)
    $pop = $systemPage.GetType().GetField('pop', $private).GetValue($systemPage)
    $getState = [Windows.Forms.Control].GetMethod('GetState', $private)
    Assert (-not $getState.Invoke($pop, @([int]2))) 'Uninstall offered to pop out the wrong floating card.'
} finally {
    if ($field) { $field.SetValue($edit, $null) }
    $main.Dispose(); $page.Dispose(); $tabs.Dispose(); $edit.Dispose()
}
Write-Host 'DOCUMENT SAFETY OK: Cancel, failed save, successful save, tab veto, empty actions and navigation guards'
