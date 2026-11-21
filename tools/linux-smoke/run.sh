#!/usr/bin/env bash
# Linux smoke checks (no Windows needed): C# 5 compile of all sources + saved-work logic under Mono.
# Needs: apt-get install -y mono-devel xvfb.  Usage: tools/linux-smoke/run.sh
set -e
here="$(cd "$(dirname "$0")" && pwd)"; root="$(cd "$here/../.." && pwd)"
export SMOKE_DIR="$(mktemp -d)"
W="$root/lib/webview2"
REFS="-r:System.dll -r:System.Core.dll -r:System.Xml.dll -r:System.Xml.Linq.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:System.Web.Extensions.dll -r:System.IO.Compression.dll -r:System.Data.dll -r:Microsoft.CSharp.dll -r:$W/Microsoft.Web.WebView2.Core.dll -r:$W/Microsoft.Web.WebView2.WinForms.dll"
mcs -langversion:5 -nologo -target:library -out:"$SMOKE_DIR/app.dll" $REFS "$root"/src/*.cs 2>&1 | grep -E "error|Compilation"
cp "$W"/*.dll "$SMOKE_DIR/"
for t in SafeOpen CronAndMove DataMove; do
  mcs -nologo -out:"$SMOKE_DIR/$t.exe" "$here/$t.cs" >/dev/null
  echo "== $t"; xvfb-run -a mono "$SMOKE_DIR/$t.exe" 2>&1 | grep -v "^\s*at \|Gtk"
done
