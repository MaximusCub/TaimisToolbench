<#
.SYNOPSIS
    Window lookup and synthetic input for the screenshot sandbox, scoped to
    process ids the caller names.

.DESCRIPTION
    Dot-source this from a sandbox script. It replaces the title-matching
    helpers in preflight\gatekit.ps1.

    A title is not an identity. The developer's own Blish HUD overlay carries
    the window title "Blish HUD", at 0,0,3440,1440, over Guild Wars 2 at the
    same rectangle. A lookup that matches that title and takes the first hit
    can return the live overlay, and synthetic clicks aimed at the sandbox
    then land in the running game. Every lookup here matches on the owning
    process id instead, and a title, when given, is an extra condition rather
    than the identity.

    Two rules hold throughout:

    Refuse rather than guess. A lookup that finds no window, or more than
    one, returns IntPtr.Zero and leaves the rectangle zeroed. There is no
    first-match fallback anywhere in this file.

    Fail closed. Input primitives compare the foreground window's process id
    against the allowed set, which starts empty, so an input call made before
    Set-SandboxTarget throws instead of typing into whatever has focus.
#>

Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices;
public static class SB {
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT rc);
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool r);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  delegate bool EnumProc(IntPtr h, IntPtr l);

  // Result of the last Find. Matches is what callers test to tell "no such
  // window" from "several, so the answer is ambiguous".
  public static IntPtr Hwnd; public static RECT R; public static int Matches;

  // Process ids whose windows may hold focus while synthetic input is sent.
  // Empty until Set-SandboxTarget runs, and empty means every Gate throws.
  public static int[] Allowed = new int[0];

  public static int PidOf(IntPtr h) { uint p; GetWindowThreadProcessId(h, out p); return (int)p; }
  public static int FgPid() { return PidOf(GetForegroundWindow()); }
  public static string TitleOf(IntPtr h) { var sb = new StringBuilder(256); GetWindowText(h, sb, 256); return sb.ToString(); }
  public static string FgTitle() { return TitleOf(GetForegroundWindow()); }

  // Visible titled windows owned by pid, optionally narrowed to an exact
  // title. Enumeration always runs to completion: stopping at the first hit
  // is what makes an ambiguous result look like a definite one.
  public static IntPtr Find(int pid, string exactTitle) {
    Hwnd = IntPtr.Zero; R = new RECT(); Matches = 0;
    IntPtr first = IntPtr.Zero;
    EnumWindows((h, l) => {
      if (!IsWindowVisible(h)) return true;
      if (PidOf(h) != pid) return true;
      string t = TitleOf(h);
      if (t.Length == 0) return true;
      if (exactTitle != null && exactTitle.Length > 0 && t != exactTitle) return true;
      Matches++;
      if (Matches == 1) first = h;
      return true;
    }, IntPtr.Zero);
    if (Matches == 1) { Hwnd = first; GetWindowRect(first, out R); }
    return Hwnd;
  }

  // Every visible titled window of a process, as one string, for the error
  // message an ambiguous or empty Find produces.
  public static string Describe(int pid) {
    var acc = new StringBuilder();
    EnumWindows((h, l) => {
      if (!IsWindowVisible(h)) return true;
      if (PidOf(h) != pid) return true;
      string t = TitleOf(h);
      if (t.Length == 0) return true;
      RECT rc; GetWindowRect(h, out rc);
      acc.AppendLine(string.Format("    hwnd={0} rect={1},{2} {3}x{4} title='{5}'",
        h, rc.L, rc.T, rc.R - rc.L, rc.B - rc.T, t));
      return true;
    }, IntPtr.Zero);
    if (acc.Length == 0) return "    (no visible titled window)";
    return acc.ToString();
  }

  static void Gate() {
    int fp = FgPid();
    for (int i = 0; i < Allowed.Length; i++) { if (Allowed[i] == fp) return; }
    var acc = new StringBuilder();
    for (int i = 0; i < Allowed.Length; i++) { if (i > 0) acc.Append(","); acc.Append(Allowed[i]); }
    string allowed = acc.Length == 0 ? "(none set)" : acc.ToString();
    throw new Exception("FOREGROUND-ABORT: focus is pid " + fp + " '" + FgTitle()
      + "', allowed pids are " + allowed);
  }

  public static void Click(int x, int y) {
    Gate();
    SetCursorPos(x, y); System.Threading.Thread.Sleep(120);
    Gate();
    mouse_event(2,0,0,0,UIntPtr.Zero); System.Threading.Thread.Sleep(60); mouse_event(4,0,0,0,UIntPtr.Zero);
  }

  public static void Wheel(int x, int y, int clicks, int dir) {
    Gate();
    SetCursorPos(x, y); System.Threading.Thread.Sleep(150);
    for (int i = 0; i < clicks; i++) { Gate(); mouse_event(0x0800, 0, 0, unchecked((uint)(dir * 120)), UIntPtr.Zero); System.Threading.Thread.Sleep(80); }
  }

  public static void Hover(int x, int y) { Gate(); SetCursorPos(x, y); }

  public static void Cap(string path, int x, int y, int w, int h) {
    Gate();
    var bmp = new System.Drawing.Bitmap(w, h);
    var gr = System.Drawing.Graphics.FromImage(bmp);
    gr.CopyFromScreen(x, y, 0, 0, bmp.Size);
    bmp.Save(path); gr.Dispose(); bmp.Dispose();
  }

  // Distinct colours in a screen region. The sandbox uses it to tell drawn
  // interface from bare Paint canvas, which is one flat colour.
  public static int DistinctColours(int x, int y, int w, int h) {
    var bmp = new System.Drawing.Bitmap(w, h);
    var gr = System.Drawing.Graphics.FromImage(bmp);
    gr.CopyFromScreen(x, y, 0, 0, bmp.Size);
    var seen = new System.Collections.Generic.HashSet<int>();
    for (int px = 0; px < w; px++) {
      for (int py = 0; py < h; py++) { seen.Add(bmp.GetPixel(px, py).ToArgb()); }
    }
    gr.Dispose(); bmp.Dispose();
    return seen.Count;
  }
}
'@ -ReferencedAssemblies System.Drawing

<#
.SYNOPSIS
    Names the processes whose windows may hold focus during synthetic input.
#>
function Set-SandboxTarget {
    param([Parameter(Mandatory = $true)][int[]]$ProcessId)
    [SB]::Allowed = [int[]]$ProcessId
}

<#
.SYNOPSIS
    The one visible window of a process, or a throw naming what it saw.
#>
function Find-SandboxWindow {
    param(
        [Parameter(Mandatory = $true)][int]$ProcessId,
        [string]$Title = '',
        [string]$What = 'window'
    )
    $null = [SB]::Find($ProcessId, $Title)
    $matched = [SB]::Matches
    if ($matched -ne 1) {
        $qualifier = if ($Title) { " titled '$Title'" } else { '' }
        $reason = if ($matched -eq 0) { 'no visible window' } else { "$matched visible windows, so the target is ambiguous" }
        throw ("Cannot identify the sandbox $What. Process $ProcessId has $reason$qualifier." +
               [Environment]::NewLine + [SB]::Describe($ProcessId))
    }
    [PSCustomObject]@{
        Hwnd      = [SB]::Hwnd
        ProcessId = $ProcessId
        Title     = [SB]::TitleOf([SB]::Hwnd)
        X         = [SB]::R.L
        Y         = [SB]::R.T
        Width     = [SB]::R.R - [SB]::R.L
        Height    = [SB]::R.B - [SB]::R.T
    }
}

<#
.SYNOPSIS
    Waits for a process to show exactly one visible window.
#>
function Wait-SandboxWindow {
    param(
        [Parameter(Mandatory = $true)][int]$ProcessId,
        [string]$Title = '',
        [string]$What = 'window',
        [int]$TimeoutSeconds = 30
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (-not (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)) {
            throw "The sandbox $What (pid $ProcessId) exited before its window appeared."
        }
        $null = [SB]::Find($ProcessId, $Title)
        if ([SB]::Matches -eq 1) { return Find-SandboxWindow -ProcessId $ProcessId -Title $Title -What $What }
        Start-Sleep -Milliseconds 500
    }
    # One more call, so the failure carries the same detail as any other.
    Find-SandboxWindow -ProcessId $ProcessId -Title $Title -What $What
}

<#
.SYNOPSIS
    Brings a process's window to the front and proves it got there.
#>
function Focus-SandboxWindow {
    param(
        [Parameter(Mandatory = $true)][int]$ProcessId,
        [string]$Title = '',
        [string]$What = 'window'
    )
    $window = Find-SandboxWindow -ProcessId $ProcessId -Title $Title -What $What
    if ([SB]::IsIconic($window.Hwnd)) { $null = [SB]::ShowWindow($window.Hwnd, 9) }
    $null = [SB]::SetForegroundWindow($window.Hwnd)
    Start-Sleep -Milliseconds 500
    $fgPid = [SB]::FgPid()
    if ($fgPid -ne $ProcessId) {
        throw "Windows refused activation of the sandbox $What (pid $ProcessId). Focus is pid $fgPid '$([SB]::FgTitle())'."
    }
    $window
}
