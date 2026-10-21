#!/usr/bin/env bash
# Compare the two-client multiplayer test logs (docs/testing.md, "Two-client multiplayer test") and print one
# PASS/FAIL/SKIP line per check. Exit status 1 when any check fails.
#   tools/mp-check.sh [mp-dir]   (default: $HOME/.var/app/com.valvesoftware.Steam/peakxr-mp)
# Tolerances: POS_TOL (m, default 1.0) for the player's position, LAND_TOL (m, default 1.5) for dropped/thrown items.
set -euo pipefail

DIR="${1:-$HOME/.var/app/com.valvesoftware.Steam/peakxr-mp}"
POS_TOL="${POS_TOL:-1.0}"
LAND_TOL="${LAND_TOL:-1.5}"
A="$DIR/a-checkpoints.jsonl"
B="$DIR/b-observer.jsonl"
SUM="$DIR/b-summary.json"
DONE="$DIR/a-done.json"

for f in "$A" "$B" "$SUM"; do
    [[ -s "$f" ]] || { echo "FAIL missing $f"; exit 1; }
done

checks() {
# A's checkpoints, each joined with B's checkpoint of the same name, become one result line per check.
jq -nr --slurpfile a "$A" --slurpfile b "$B" --slurpfile s "$SUM" \
    --argjson pos "$POS_TOL" --argjson land "$LAND_TOL" '
def dist($p; $q): [range(0; 3) | ($p[.] - $q[.]) * ($p[.] - $q[.])] | add | sqrt;
def r2: . * 100 | round / 100;
($b | map(select(.type == "checkpoint")) | map({key: .name, value: .}) | from_entries) as $bcp
| ($s[0]) as $sum
| ($a | map(select(.name == "join")) | first) as $join
| [
    (if ($b | any(.type == "join")) then "PASS B saw A join"
     else "FAIL B never saw a remote player join" end),
    (if $join == null then "FAIL A wrote no join checkpoint"
     elif $join.versionOk and $join.inLobby and $join.masterIsHost then
       "PASS A version check: lobby \($join.lobbyVersion) = local \($join.localVersion), in lobby, master is host"
     else "FAIL A join: versionOk=\($join.versionOk) inLobby=\($join.inLobby) masterIsHost=\($join.masterIsHost) (\($join.note))" end),
    ($a[] | . as $x | $bcp[$x.name] as $y |
      if $x.status == "skip" then "SKIP \($x.name): \($x.note)"
      elif $x.status != "pass" then "FAIL \($x.name): A step failed: \($x.note)"
      elif $y == null then "FAIL \($x.name): B logged no checkpoint"
      elif ($y.remotePresent | not) then "FAIL \($x.name): B saw no remote player"
      else
        (if $x.pos == null or $y.pos == null then "FAIL \($x.name) position: missing"
         else dist($x.pos; $y.pos) as $d
           | "\(if $d <= $pos then "PASS" else "FAIL" end) \($x.name) position: \($d | r2) m apart" end),
        (if $x.heldId == $y.itemId then "PASS \($x.name) held item: \($y.item // "none")"
         else "FAIL \($x.name) held item: A \($x.item // "none") (\($x.heldId)), B sees \($y.item // "none") (\($y.itemId))" end),
        (if $x.landing == null then empty
         elif ($y.groundItems // []) == [] then "FAIL \($x.name) landing: B sees no item \($x.itemId) on the ground"
         else ([$y.groundItems[] | dist(.; $x.landing)] | min) as $d
           | "\(if $d <= $land then "PASS" else "FAIL" end) \($x.name) landing: nearest item \($d | r2) m from where A saw it" end)
      end),
    (if $sum.exceptions == 0 then "PASS B logged no exceptions"
     else "FAIL B logged \($sum.exceptions) exception(s): \($sum.firstExceptions | map(.e) | join(" | "))" end),
    (if $sum.unknownEventErrors == 0 then "PASS B logged no unknown-event errors"
     else "FAIL B logged \($sum.unknownEventErrors) unknown-event error(s)" end),
    (if $sum.event151 == 0 then "PASS B received no event 151"
     else "FAIL B received event 151 \($sum.event151) time(s)" end),
    "INFO B ended: \($sum.endReason); \($sum.checkpoints) checkpoints, \($sum.samples) samples, \($sum.errors) logged errors"
  ]
| .[]
' || echo "FAIL checker: jq could not read the logs"

if [[ -s "$DONE" ]]; then
    jq -r '"\(if .sent151 == 0 then "PASS" else "FAIL" end) A sent event 151 \(.sent151) time(s)",
        "INFO A ended: \(.reason); \(.pass) pass, \(.fail) fail, \(.skip) skip, vr=\(.vr)"' "$DONE"
else
    echo "FAIL a-done.json missing: the VR side did not finish"
fi
}

out="$(checks)"
echo "$out"
! grep -q '^FAIL' <<<"$out"
